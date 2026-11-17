using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ReStore.Domain.Common;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Domain.Interfaces.Repository
{
    /// <summary>
    /// The part of a read repository that can be used without knowing the entity type.
    /// <see cref="CheckKeyExistsAttribute"/> needs this: the attribute can only name a
    /// repository as a <see cref="Type"/>, so the caller cannot be generic over the
    /// entity at compile time.
    /// </summary>
    public interface INonGenericQueryRepositoryBase
    {
        string IdPropertyName { get; }

        Task<DomainResponse> IsKeyExistsAsync(
            object id,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Read side. Separated from the write side on purpose: a query handler only
    /// ever needs this, and reads use AsNoTracking.
    /// </summary>
    public interface IGenericQueryRepositoryBase<Entity> : INonGenericQueryRepositoryBase
        where Entity : BaseEntity
    {
        Task<DomainResponseOne<Entity>> GetByIdAsync(
            object id,
            RequestConfig<Entity>? requestConfig = null,
            CancellationToken cancellationToken = default);

        Task<DomainResponseList<Entity>> GetByIdsAsync(
            IEnumerable<object> ids,
            RequestConfig<Entity>? requestConfig = null,
            CancellationToken cancellationToken = default);

        Task<DomainResponseList<Entity>> ListAsync(
            RequestConfig<Entity>? requestConfig = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Write side. Every write method takes an optional <c>afterSave</c> callback
    /// that runs *inside* the transaction with the changed entries, which is where
    /// cross-aggregate guards and audit entries belong: returning a failed
    /// DomainResponse from it rolls the whole write back.
    /// </summary>
    public interface IGenericCommandRepositoryBase<Entity> where Entity : BaseEntity
    {
        string IdPropertyName { get; }

        Task<DomainResponse> AddAsync(
            Entity entity,
            CancellationToken cancellationToken = default);

        Task<DomainResponse> AddAsync(
            Entity entity,
            AfterSaveCallback afterSave,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<DomainResponse>> AddRangeAsync(
            IEnumerable<Entity> entities,
            CancellationToken cancellationToken = default);

        Task<DomainResponse> UpdateAsync(
            Entity entity,
            IReadOnlyCollection<string>? changeFields,
            CancellationToken cancellationToken = default);

        Task<DomainResponse> UpdateAsync(
            Entity entity,
            IReadOnlyCollection<string>? changeFields,
            AfterSaveCallback afterSave,
            CancellationToken cancellationToken = default);

        Task<DomainResponse> DeleteAsync(
            Entity entity,
            CancellationToken cancellationToken = default);

        Task<DomainResponse> DeleteAsync(
            Entity entity,
            AfterSaveCallback afterSave,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Runs after SaveChanges, still inside the transaction. <paramref name="tran"/>
    /// is the entry point for anything the repository cannot see (a second
    /// DbContext, an HTTP guard, an audit writer). A failed response rolls back.
    /// </summary>
    public delegate Task<DomainResponse> AfterSaveCallback(
        object? tran,
        IEnumerable<EntityChangeEntry> changedEntries);
}
