using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ReStore.Application.Common;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories
{
    /// <summary>
    /// Write-side generic repository.
    ///
    /// Two behaviours matter more than the rest:
    /// <list type="bullet">
    /// <item><b>Partial update.</b> <c>changeFields</c> is applied by attaching the
    /// entity and marking only those properties as modified, so EF emits an UPDATE
    /// with exactly those columns — even though the caller sent a whole form.</item>
    /// <item><b>afterSave inside the transaction.</b> The callback runs before the
    /// commit and can roll it back by returning a failed response, which is how a
    /// cross-aggregate guard or a second write stays atomic with the first.</item>
    /// </list>
    /// </summary>
    public class GenericCommandRepositoryBase<Entity> : IGenericCommandRepositoryBase<Entity>
        where Entity : BaseEntity
    {
        private readonly StoreContext _context;

        public GenericCommandRepositoryBase(StoreContext context)
        {
            _context = context;
            IdPropertyName = EntityKeyResolver.Resolve<Entity>(context);
        }

        public string IdPropertyName { get; }

        public Task<DomainResponse> AddAsync(Entity entity, CancellationToken cancellationToken = default)
            => AddAsync(entity, null, cancellationToken);

        public Task<DomainResponse> AddAsync(
            Entity entity,
            AfterSaveCallback? afterSave,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(
                entity,
                () =>
                {
                    _context.Set<Entity>().Add(entity);
                    return Task.CompletedTask;
                },
                new Dictionary<string, (object? OldValue, object? NewValue)>(),
                afterSave,
                cancellationToken);
        }

        public async Task<IEnumerable<DomainResponse>> AddRangeAsync(
            IEnumerable<Entity> entities,
            CancellationToken cancellationToken = default)
        {
            var results = new List<DomainResponse>();
            foreach (var entity in entities)
                results.Add(await AddAsync(entity, null, cancellationToken));

            return results;
        }

        public async Task<IEnumerable<DomainResponse>> AddRangeAsync(
            IEnumerable<Entity> entities,
            AfterSaveCallback? afterSave,
            CancellationToken cancellationToken = default)
        {
            var results = new List<DomainResponse>();
            foreach (var entity in entities)
                results.Add(await AddAsync(entity, afterSave, cancellationToken));

            return results;
        }

        public Task<DomainResponse> UpdateAsync(
            Entity entity,
            IReadOnlyCollection<string>? changeFields,
            CancellationToken cancellationToken = default)
            => UpdateAsync(entity, changeFields, null, cancellationToken);

        public Task<DomainResponse> UpdateAsync(
            Entity entity,
            IReadOnlyCollection<string>? changeFields,
            AfterSaveCallback? afterSave,
            CancellationToken cancellationToken = default)
        {
            if (changeFields is null || changeFields.Count == 0)
                return Task.FromResult(new DomainResponse("No columns were selected for update."));

            // Attach first: IsModified can only be set on a tracked entry. Attaching
            // marks the graph Unchanged, so nothing is written until we say so.
            if (_context.Entry(entity).State == EntityState.Detached)
                _context.Set<Entity>().Attach(entity);

            var entry = _context.Entry(entity);
            var unknown = new List<string>();

            foreach (var field in changeFields)
            {
                var property = entry.Metadata.FindProperty(field);
                if (property is null)
                {
                    unknown.Add(field);
                    continue;
                }

                entry.Property(property.Name).IsModified = true;
            }

            if (unknown.Count > 0)
                return Task.FromResult(new DomainResponse(
                    $"Unknown column(s) for {typeof(Entity).Name}: {string.Join(", ", unknown)}."));

            return ExecuteAsync(
                entity,
                () => Task.CompletedTask,
                ChangedProperties(entry),
                afterSave,
                cancellationToken);
        }

        public Task<DomainResponse> DeleteAsync(Entity entity, CancellationToken cancellationToken = default)
            => DeleteAsync(entity, null, cancellationToken);

        public Task<DomainResponse> DeleteAsync(
            Entity entity,
            AfterSaveCallback? afterSave,
            CancellationToken cancellationToken = default)
            => ExecuteAsync(
                entity,
                () =>
                {
                    _context.Set<Entity>().Remove(entity);
                    return Task.CompletedTask;
                },
                new Dictionary<string, (object? OldValue, object? NewValue)>(),
                afterSave,
                cancellationToken);

        /// <summary>
        /// Saves, optionally wraps everything in a transaction with the afterSave hook,
        /// and turns any failure into a DomainResponse rather than an exception.
        /// </summary>
        private async Task<DomainResponse> ExecuteAsync(
            Entity entity,
            Func<Task> stage,
            IReadOnlyDictionary<string, (object? OldValue, object? NewValue)> changes,
            AfterSaveCallback? afterSave,
            CancellationToken cancellationToken)
        {
            // Without a callback a single SaveChanges is already atomic, so no explicit
            // transaction is opened (this also keeps the InMemory provider usable).
            var transaction = afterSave is null
                ? null
                : await _context.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                await stage();

                var saved = await _context.SaveChangesAsync(cancellationToken);

                if (afterSave is not null)
                {
                    var entries = new[]
                    {
                        new EntityChangeEntry(typeof(Entity).Name, KeyOf(entity), KindOf(entity), changes)
                    };

                    var afterSaveResult = await afterSave(transaction, entries);
                    if (afterSaveResult.Result is false)
                    {
                        if (transaction is not null)
                            await transaction.RollbackAsync(cancellationToken);

                        _context.ChangeTracker.Clear();
                        return afterSaveResult;
                    }
                }

                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);

                // Read the key while the entity is still ours: for an add, EF has just
                // filled it in; for everything else we set it ourselves.
                var key = KeyOf(entity);

                _context.ChangeTracker.Clear();

                return new DomainResponse(key, saved);
            }
            catch (Exception exception)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(CancellationToken.None);

                _context.ChangeTracker.Clear();

                return new DomainResponse(exception);
            }
            finally
            {
                if (transaction is not null)
                    await transaction.DisposeAsync();
            }
        }

        private int KeyOf(Entity entity)
        {
            var value = EntityReflection.GetKeyValue(entity, IdPropertyName);
            return value is null ? 0 : Convert.ToInt32(value);
        }

        private EntityChangeKind KindOf(Entity entity)
            => _context.Entry(entity).State switch
            {
                EntityState.Added => EntityChangeKind.Added,
                EntityState.Deleted => EntityChangeKind.Deleted,
                _ => EntityChangeKind.Modified
            };

        private static Dictionary<string, (object? OldValue, object? NewValue)> ChangedProperties(
            EntityEntry entry)
        {
            var changed = new Dictionary<string, (object?, object?)>(StringComparer.Ordinal);

            foreach (var property in entry.Properties.Where(property => property.IsModified))
                changed[property.Metadata.Name] = (property.OriginalValue, property.CurrentValue);

            return changed;
        }
    }
}
