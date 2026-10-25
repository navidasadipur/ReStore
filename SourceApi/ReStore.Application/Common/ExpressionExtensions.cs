using System;
using System.Linq.Expressions;

namespace ReStore.Application.Common
{
    /// <summary>
    /// Expression composition for query criteria. A handler receives optional pieces
    /// (id filter, entity-specific criteria, free-text search) and must combine them
    /// into one predicate EF can translate; this is that one place.
    /// </summary>
    public static class ExpressionExtensions
    {
        public static Expression<Func<T, bool>>? And<T>(
            this Expression<Func<T, bool>>? left,
            Expression<Func<T, bool>>? right)
        {
            if (left is null) return right;
            if (right is null) return left;

            var parameter = Expression.Parameter(typeof(T), "entity");
            return Expression.Lambda<Func<T, bool>>(
                Expression.AndAlso(
                    new ParameterReplacer(left.Parameters[0], parameter).Visit(left.Body),
                    new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body)),
                parameter);
        }

        public static Expression<Func<T, bool>>? Or<T>(
            this Expression<Func<T, bool>>? left,
            Expression<Func<T, bool>>? right)
        {
            if (left is null) return right;
            if (right is null) return left;

            var parameter = Expression.Parameter(typeof(T), "entity");
            return Expression.Lambda<Func<T, bool>>(
                Expression.OrElse(
                    new ParameterReplacer(left.Parameters[0], parameter).Visit(left.Body),
                    new ParameterReplacer(right.Parameters[0], parameter).Visit(right.Body)),
                parameter);
        }

        /// <summary>Swaps the parameter of one predicate for another so two lambdas can be joined.</summary>
        private sealed class ParameterReplacer : ExpressionVisitor
        {
            private readonly ParameterExpression _from;
            private readonly ParameterExpression _to;

            public ParameterReplacer(ParameterExpression from, ParameterExpression to)
            {
                _from = from;
                _to = to;
            }

            protected override Expression VisitParameter(ParameterExpression node)
                => node == _from ? _to : base.VisitParameter(node);
        }
    }
}
