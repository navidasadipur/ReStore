using System.Collections.Generic;

namespace ReStore.Domain.Interfaces.Common
{
    public enum EntityChangeKind
    {
        Added = 0,
        Modified = 1,
        Deleted = 2
    }

    /// <summary>
    /// One entity touched by a write, handed to an <c>afterSave</c> callback so the
    /// callback can act on what actually changed without re-reading the row.
    /// </summary>
    public class EntityChangeEntry
    {
        public EntityChangeEntry(
            string entityName,
            object? key,
            EntityChangeKind kind,
            IReadOnlyDictionary<string, (object? OldValue, object? NewValue)>? changes = null)
        {
            EntityName = entityName;
            Key = key;
            Kind = kind;
            Changes = changes ?? new Dictionary<string, (object?, object?)>();
        }

        public string EntityName { get; }

        public object? Key { get; }

        public EntityChangeKind Kind { get; }

        /// <summary>Property name to (old, new). Empty for Added/Deleted.</summary>
        public IReadOnlyDictionary<string, (object? OldValue, object? NewValue)> Changes { get; }
    }
}
