using Microsoft.EntityFrameworkCore;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories
{
    /// <summary>
    /// Product reads that the generic repository cannot express: DISTINCT lists for the
    /// filter controls, and an existence check on the image id. Everything else is
    /// inherited, so this is also a valid target for [CheckKeyExists].
    /// </summary>
    public sealed class ProductQueryRepository
        : GenericQueryRepositoryBase<ProductEntity>, IProductQueryRepository
    {
        private readonly StoreContext _context;

        public ProductQueryRepository(StoreContext context, IMapperCustome mapper)
            : base(context, mapper)
            => _context = context;

        public async Task<DomainResponseOne<ProductFilterDto>> GetFiltersAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                var brands = await _context.Products
                    .AsNoTracking()
                    .Select(product => product.Brand)
                    .Distinct()
                    .OrderBy(brand => brand)
                    .ToListAsync(cancellationToken);

                var types = await _context.Products
                    .AsNoTracking()
                    .Select(product => product.Type)
                    .Distinct()
                    .OrderBy(type => type)
                    .ToListAsync(cancellationToken);

                return new DomainResponseOne<ProductFilterDto>(
                    new ProductFilterDto { Brands = brands, Types = types });
            }
            catch (Exception exception)
            {
                return new DomainResponseOne<ProductFilterDto>(exception);
            }
        }

        public Task<bool> IsPublicIdInUseAsync(string publicId, CancellationToken cancellationToken = default)
            => _context.Products
                .AsNoTracking()
                .AnyAsync(product => product.publicId == publicId, cancellationToken);
    }
}
