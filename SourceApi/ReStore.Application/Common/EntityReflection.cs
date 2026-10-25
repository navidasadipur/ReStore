using System;
using System.Reflection;

namespace ReStore.Application.Common
{
    /// <summary>
    /// Key access without assuming the key is called "Id". The property *name* comes
    /// from EF metadata (via the repository's IdPropertyName), so an entity with a
    /// different key name keeps working.
    /// </summary>
    public static class EntityReflection
    {
        public static PropertyInfo? FindKeyProperty(Type entityType, string keyPropertyName)
            => entityType.GetProperty(keyPropertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

        public static object? GetKeyValue(object entity, string keyPropertyName)
        {
            var property = FindKeyProperty(entity.GetType(), keyPropertyName);
            if (property is null)
                throw new InvalidOperationException(
                    $"{entity.GetType().Name} has no public property named '{keyPropertyName}'.");

            return property.GetValue(entity);
        }

        public static int GetKeyValueAsInt(object entity, string keyPropertyName)
        {
            var value = GetKeyValue(entity, keyPropertyName);
            return value is null ? 0 : Convert.ToInt32(value);
        }

        public static void SetKeyValue(object entity, string keyPropertyName, int id)
        {
            var property = FindKeyProperty(entity.GetType(), keyPropertyName);
            if (property is null || !property.CanWrite)
                throw new InvalidOperationException(
                    $"{entity.GetType().Name} has no writable property named '{keyPropertyName}'.");

            var target = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            property.SetValue(entity, target == typeof(int) ? id : Convert.ChangeType(id, target));
        }
    }
}
