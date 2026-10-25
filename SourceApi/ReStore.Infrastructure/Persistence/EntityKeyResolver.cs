using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ReStore.Domain.Common;

namespace ReStore.Infrastructure.Persistence
{
    /// <summary>
    /// Resolves an entity's key property name from EF metadata.
    ///
    /// This is why <see cref="BaseEntity"/> needs no [Key] attribute and why Domain
    /// needs no EF reference: the knowledge lives where the mapping already is.
    /// </summary>
    internal static class EntityKeyResolver
    {
        public static string Resolve<TEntity>(StoreContext context) where TEntity : BaseEntity
            => Resolve(context, typeof(TEntity));

        public static string Resolve(StoreContext context, Type entityType)
        {
            var mapped = context.Model.FindEntityType(entityType)
                ?? throw new InvalidOperationException(
                    $"{entityType.Name} is not mapped in {nameof(StoreContext)}.");

            var primaryKey = mapped.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"{entityType.Name} has no primary key.");

            return primaryKey.Properties[0].Name;
        }
    }
}
