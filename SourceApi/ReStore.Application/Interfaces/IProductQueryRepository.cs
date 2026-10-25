using ReStore.Application.DTOs;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// Product-specific reads that the generic repository cannot express. It extends the
    /// generic query contract, so it can also be named by a command's
    /// [CheckKeyExists(typeof(IProductQueryRepository))].
    /// </summary>
    public interface IProductQueryRepository : IGenericQueryRepositoryBase<ProductEntity>
    {
        /// <summary>Distinct brands and types, for building the filter controls.</summary>
        Task<DomainResponseOne<ProductFilterDto>> GetFiltersAsync(CancellationToken cancellationToken = default);

        /// <summary>True when another product already uses this image public id (used before deleting one).</summary>
        Task<bool> IsPublicIdInUseAsync(string publicId, CancellationToken cancellationToken = default);
    }
}
