using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ReStore.Application.Controls
{
    /// <summary>
    /// Reads <see cref="ControlAttribute"/> off an entity's properties and validates
    /// the values. Extracted from BaseControls so it can be tested on its own with a
    /// throwaway annotated type, without a database or a repository.
    ///
    /// Every failure is collected into one DomainResponse: a user filling a form
    /// should see all the problems at once, not the first one.
    /// </summary>
    public static class ControlAttributeValidator
    {
        public static DomainResponse Validate(
            object instance,
            IReadOnlyCollection<string>? onlyProperties = null)
        {
            ArgumentNullException.ThrowIfNull(instance);

            var response = new DomainResponse();

            foreach (var (property, attribute) in AnnotatedProperties(instance.GetType()))
            {
                if (onlyProperties is not null
                    && onlyProperties.Count > 0
                    && !onlyProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var error = ValidateProperty(attribute, property.GetValue(instance), property.Name);
                if (error is not null)
                    response = new DomainResponse(error).AddTo(response);
            }

            return response;
        }

        public static IEnumerable<(PropertyInfo Property, ControlAttribute Attribute)> AnnotatedProperties(Type type)
            => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                   .Select(property => (Property: property,
                                        Attribute: property.GetCustomAttribute<ControlAttribute>()))
                   .Where(pair => pair.Attribute is not null)
                   .Select(pair => (pair.Property, pair.Attribute!));

        private static string? ValidateProperty(ControlAttribute attribute, object? value, string propertyName)
        {
            var displayName = string.IsNullOrWhiteSpace(attribute.DisplayName)
                ? propertyName
                : attribute.DisplayName!;

            var text = value?.ToString()?.Trim() ?? string.Empty;

            if (attribute.Required && string.IsNullOrWhiteSpace(text))
                return $"{displayName} is required.";

            // An optional property that was left empty passes: the other rules are
            // about the shape of a value that was supplied.
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (attribute.MinLength > 0 && text.Length < attribute.MinLength)
                return $"{displayName} must be at least {attribute.MinLength} characters.";

            if (attribute.MaxLength > 0 && text.Length > attribute.MaxLength)
                return $"{displayName} must be at most {attribute.MaxLength} characters.";

            if (!string.IsNullOrWhiteSpace(attribute.RegexPattern)
                && !Regex.IsMatch(text, attribute.RegexPattern))
            {
                return string.IsNullOrWhiteSpace(attribute.RegexErrorMessage)
                    ? $"{displayName} is not in the expected format."
                    : attribute.RegexErrorMessage;
            }

            if (attribute.HasMinVal || attribute.HasMaxVal)
            {
                if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                    return $"{displayName} must be a number.";

                if (attribute.HasMinVal && number < attribute.MinVal)
                    return $"{displayName} must not be less than {attribute.MinVal}.";

                if (attribute.HasMaxVal && number > attribute.MaxVal)
                    return $"{displayName} must not be greater than {attribute.MaxVal}.";
            }

            return null;
        }
    }
}
