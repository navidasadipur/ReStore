using System;

namespace ReStore.Domain.Common
{
    /// <summary>
    /// Declarative validation metadata for an entity property, read by
    /// ControlAttributeValidator when a row is added or updated.
    ///
    /// Ported from AccountsApi, minus the Iran-specific field types (Mobile,
    /// NationalCode) and the CurrentAccountNumber variant, which have no meaning
    /// in ReStore.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class ControlAttribute : Attribute
    {
        public ControlAttribute()
        {
        }

        public ControlAttribute(string displayName)
        {
            DisplayName = displayName;
        }

        public ControlAttribute(string displayName, bool required) : this(displayName)
        {
            Required = required;
        }

        public ControlAttribute(string displayName, bool required, int minLength, int maxLength)
            : this(displayName, required)
        {
            MinLength = minLength;
            MaxLength = maxLength;
        }

        public ControlAttribute(string displayName, int minValue, int maxValue)
            : this(displayName)
        {
            HasMinVal = true;
            HasMaxVal = true;
            MinVal = minValue;
            MaxVal = maxValue;
        }

        /// <summary>Name used in the error message; falls back to the property name.</summary>
        public string? DisplayName { get; set; }

        public bool Required { get; set; }

        /// <summary>Minimum string length; 0 means "not checked".</summary>
        public int MinLength { get; set; }

        /// <summary>Maximum string length; 0 means "not checked".</summary>
        public int MaxLength { get; set; }

        public string? RegexPattern { get; set; }

        public string? RegexErrorMessage { get; set; }

        public bool HasMinVal { get; set; }

        public bool HasMaxVal { get; set; }

        public int MinVal { get; set; }

        public int MaxVal { get; set; }
    }
}
