using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using ReStore.Application.Common;

namespace ReStore.Application.Filters
{
    /// <summary>
    /// Turns the client's filter list into one <c>Expression&lt;Func&lt;Entity, bool&gt;&gt;</c>.
    ///
    /// Ported from AccountsApi's <c>IFilterOperate</c>, with one deliberate difference:
    /// upstream reflected over the <b>DTO</b> and translated its names onto the entity,
    /// which needs a name map as soon as a DTO diverges from its entity. Here the filter
    /// column is matched against the <b>entity</b>, because that is what the expression
    /// is built from and what EF will translate. When DTOs start renaming columns, a
    /// mapping belongs here — not scattered through handlers.
    ///
    /// Every failure is a message, never an exception: an unknown column, an operator
    /// that does not fit the column type, or a value that will not parse all become a
    /// failed envelope naming the offender.
    /// </summary>
    public static class FilterExpressionBuilder
    {
        public static (Expression<Func<Entity, bool>>? Expression, string? Error) Build<Entity>(
            IReadOnlyCollection<FilterInfo>? filters)
        {
            if (filters is null || filters.Count == 0)
                return (null, null);

            var parameter = Expression.Parameter(typeof(Entity), "entity");
            Expression? combined = null;

            // Filters with no InnerFilterId are ANDed together: "brand is Nike AND price > 100".
            foreach (var filter in filters.Where(filter => (filter.InnerFilterId ?? 0) == 0))
            {
                var (expression, error) = BuildOne<Entity>(filter, parameter);
                if (error is not null)
                    return (null, error);

                combined = combined is null ? expression : Expression.AndAlso(combined, expression!);
            }

            // Filters sharing an InnerFilterId are ORed inside that group, then ANDed
            // with the rest: "brand is Nike OR Adidas" AND "price > 100".
            foreach (var group in filters
                         .Where(filter => (filter.InnerFilterId ?? 0) != 0)
                         .GroupBy(filter => filter.InnerFilterId!.Value))
            {
                Expression? orGroup = null;

                foreach (var filter in group)
                {
                    var (expression, error) = BuildOne<Entity>(filter, parameter);
                    if (error is not null)
                        return (null, error);

                    orGroup = orGroup is null ? expression : Expression.OrElse(orGroup, expression!);
                }

                if (orGroup is not null)
                    combined = combined is null ? orGroup : Expression.AndAlso(combined, orGroup);
            }

            return combined is null
                ? (null, null)
                : (Expression.Lambda<Func<Entity, bool>>(combined, parameter), null);
        }

        private static (Expression? Expression, string? Error) BuildOne<Entity>(
            FilterInfo filter,
            ParameterExpression parameter)
        {
            if (string.IsNullOrWhiteSpace(filter.ColName))
                return (null, "A filter was sent without a column name.");

            var property = typeof(Entity).GetProperty(
                filter.ColName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property is null)
                return (null, $"Unknown filter column '{filter.ColName}' for {typeof(Entity).Name}.");

            var member = Expression.Property(parameter, property);
            var columnType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var operatorType = filter.FilterType ?? eFilterType.ftContains;

            // Null checks do not need a value, and are meaningless on a non-nullable
            // column: they become a constant so the caller still gets a valid predicate.
            if (operatorType == eFilterType.ftIsNull)
                return IsNullable(property.PropertyType)
                    ? (Expression.Equal(member, Expression.Constant(null, property.PropertyType)), null)
                    : (Expression.Constant(false), null);

            if (operatorType == eFilterType.ftNotNull)
                return IsNullable(property.PropertyType)
                    ? (Expression.NotEqual(member, Expression.Constant(null, property.PropertyType)), null)
                    : (Expression.Constant(true), null);

            return BuildComparison(property, member, columnType, filter, operatorType);
        }

        private static (Expression? Expression, string? Error) BuildComparison(
            PropertyInfo property,
            MemberExpression member,
            Type columnType,
            FilterInfo filter,
            eFilterType operatorType)
        {
            var display = property.Name;

            // Between and In take a set of values rather than one.
            if (operatorType is eFilterType.ftBetween or eFilterType.ftIn)
            {
                var (values, valuesError) = ReadValues(filter.Value);
                if (valuesError is not null)
                    return (null, $"Filter '{display}': {valuesError}");

                if (operatorType == eFilterType.ftIn)
                    return BuildIn(member, columnType, values, display);

                if (values.Count != 2)
                    return (null, $"Filter '{display}' with ftBetween needs exactly two values.");

                var converted = new object?[2];
                for (var index = 0; index < 2; index++)
                {
                    var (value, error) = ConvertValue(values[index], columnType, display);
                    if (error is not null)
                        return (null, error);

                    converted[index] = value;
                }

                var lower = Expression.GreaterThanOrEqual(member, Expression.Constant(converted[0], property.PropertyType));
                var upper = Expression.LessThanOrEqual(member, Expression.Constant(converted[1], property.PropertyType));
                return (Expression.AndAlso(lower, upper), null);
            }

            var (single, singleError) = ConvertValue(filter.Value, columnType, display);
            if (singleError is not null)
                return (null, singleError);

            var constant = Expression.Constant(single, property.PropertyType);

            return operatorType switch
            {
                eFilterType.ftEqual or eFilterType.ftWordtoWord => (Expression.Equal(member, constant), null),
                eFilterType.ftNotEqual => (Expression.NotEqual(member, constant), null),

                eFilterType.ftGreaterThan => Comparables(member, constant, columnType, display, Expression.GreaterThan),
                eFilterType.ftLessThan => Comparables(member, constant, columnType, display, Expression.LessThan),
                eFilterType.ftGreaterThanOrEqual => Comparables(member, constant, columnType, display, Expression.GreaterThanOrEqual),
                eFilterType.ftLessThanOrEqual => Comparables(member, constant, columnType, display, Expression.LessThanOrEqual),

                eFilterType.ftContains => StringMethod(member, constant, columnType, display, nameof(string.Contains)),
                eFilterType.ftStartWith => StringMethod(member, constant, columnType, display, nameof(string.StartsWith)),
                eFilterType.ftEndWith => StringMethod(member, constant, columnType, display, nameof(string.EndsWith)),

                _ => (null, $"Operator '{operatorType}' is not supported for filter '{display}'.")
            };
        }

        private static (Expression? Expression, string? Error) Comparables(
            MemberExpression member,
            ConstantExpression constant,
            Type columnType,
            string display,
            Func<Expression, Expression, BinaryExpression> build)
        {
            if (!IsOrderable(columnType))
                return (null, $"Filter '{display}' does not support a range comparison on {columnType.Name}.");

            return (build(member, constant), null);
        }

        private static (Expression? Expression, string? Error) StringMethod(
            MemberExpression member,
            ConstantExpression constant,
            Type columnType,
            string display,
            string methodName)
        {
            if (columnType != typeof(string))
                return (null, $"Filter '{display}' does not support '{methodName}' on {columnType.Name}.");

            // Case sensitivity follows the database: Npgsql's LIKE (and .NET's Contains)
            // are case-sensitive, so a case-insensitive search needs lowering on both
            // sides in the query's own criteria — not a collation assumption here.
            var method = typeof(string).GetMethod(methodName, new[] { typeof(string) })!;
            return (Expression.Call(member, method, constant), null);
        }

        private static (Expression? Expression, string? Error) BuildIn(
            MemberExpression member,
            Type columnType,
            IReadOnlyList<object?> values,
            string display)
        {
            var listType = typeof(List<>).MakeGenericType(columnType);
            var typedList = (IList)Activator.CreateInstance(listType)!;

            foreach (var value in values)
            {
                var (converted, error) = ConvertValue(value, columnType, display);
                if (error is not null)
                    return (null, error);

                typedList.Add(converted);
            }

            var contains = Expression.Call(
                typeof(Enumerable),
                nameof(Enumerable.Contains),
                new[] { columnType },
                Expression.Constant(typedList, listType),
                member);

            return (contains, null);
        }

        /// <summary>
        /// Reads the value set for ftIn/ftBetween. Accepts what JSON actually produces
        /// (a JsonElement array), a real IEnumerable, or a comma-separated string.
        /// </summary>
        private static (List<object?> Values, string? Error) ReadValues(object? value)
        {
            switch (value)
            {
                case null:
                    return (new List<object?>(), "no value was supplied.");

                case JsonElement { ValueKind: JsonValueKind.Array } element:
                    return (element.EnumerateArray().Select(ToClr).ToList(), null);

                case JsonElement element:
                    return (new List<object?> { ToClr(element) }, null);

                case IEnumerable enumerable when value is not string:
                    return (enumerable.Cast<object?>().ToList(), null);

                case string text:
                    return (text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(part => (object?)part)
                                .ToList(), null);

                default:
                    return (new List<object?> { value }, null);
            }
        }

        private static (object? Value, string? Error) ConvertValue(object? value, Type targetType, string display)
        {
            if (value is JsonElement element)
                value = ToClr(element);

            if (value is null)
            {
                return targetType.IsValueType
                    ? (null, $"Filter '{display}' needs a value.")
                    : (null, null);
            }

            if (targetType.IsInstanceOfType(value))
                return (value, null);

            try
            {
                if (targetType == typeof(string))
                    return (Convert.ToString(value, CultureInfo.InvariantCulture), null);

                if (targetType.IsEnum)
                    return (Enum.Parse(targetType, Convert.ToString(value, CultureInfo.InvariantCulture)!, true), null);

                if (targetType == typeof(Guid))
                    return (Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!), null);

                if (targetType == typeof(DateTime))
                    return (DateTime.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), null);

                if (targetType == typeof(DateTimeOffset))
                    return (DateTimeOffset.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture), null);

                return (Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture), null);
            }
            catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
            {
                return (null, $"The value '{value}' for filter '{display}' is not a valid {targetType.Name}.");
            }
        }

        /// <summary>Unwraps a System.Text.Json value into the CLR type the filter expects.</summary>
        private static object? ToClr(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };

        private static bool IsNullable(Type type)
            => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;

        /// <summary>
        /// Types where C# — and therefore the expression tree — defines a range
        /// comparison. <c>string</c> is deliberately absent: C# has no "&gt;" operator for
        /// it, so building that node throws. Range filters on text should use
        /// ftStartWith / ftEndWith / ftContains instead.
        /// </summary>
        private static bool IsOrderable(Type type)
            => type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(DateOnly)
               || type == typeof(TimeOnly)
               || type == typeof(TimeSpan)
               || type == typeof(decimal)
               || type == typeof(double)
               || type == typeof(float)
               || type == typeof(byte)
               || type == typeof(short)
               || type == typeof(int)
               || type == typeof(long);
    }
}
