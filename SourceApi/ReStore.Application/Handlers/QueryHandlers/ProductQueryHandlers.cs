using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Handlers.BaseHandlers;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;
using ReStore.Domain.Entities;
using System.Linq.Expressions;

namespace ReStore.Application.Handlers.QueryHandlers
{
    /// <summary>
    /// The catalog list: brand/type filters, free-text search, and the UI's sort key.
    /// </summary>
    public class ProductListQueryHandler
        : BaseQueryHandler<ProductEntity, ProductListQuery,
            PaginatedResponseListDto<ProductListDto>, ProductListDto>,
          IRequestHandler<ProductListQuery, PaginatedResponseListDto<ProductListDto>>
    {
        public ProductListQueryHandler(
            IProductQueryRepository repository,
            IMapperCustome mapper,
            IAppLogger logger)
            : base(repository, mapper, logger)
        {
        }

        /// <summary>
        /// Only the columns the list renders — a catalog page must not read descriptions.
        /// QuantityInStock is in because the admin inventory grid shows it from this list.
        /// </summary>
        protected override Expression<Func<ProductEntity, object>> SelectExpr => product => new
        {
            product.ProductId,
            product.Name,
            product.Price,
            product.PictureUrl,
            product.Brand,
            product.Type,
            product.QuantityInStock
        };

        /// <summary>
        /// Case-insensitive substring search. Lowering both sides is deliberate: Npgsql's
        /// LIKE is case-sensitive, so a search box without this only matches exact case.
        /// </summary>
        protected override Expression<Func<ProductEntity, bool>> FullSearchExpr(ProductListQuery query)
        {
            var needle = (query.FullSearchPhrase ?? string.Empty).Trim().ToLowerInvariant();

            return product =>
                product.Name.ToLower().Contains(needle)
                || needle.Contains(product.Name.ToLower());
        }

        protected override Task<Expression<Func<ProductEntity, bool>>?> GetAdditionalCriteria(ProductListQuery query)
        {
            var brands = SplitList(query.Brands);
            var types = SplitList(query.Types);

            Expression<Func<ProductEntity, bool>>? criteria = null;

            if (brands.Count > 0)
                criteria = product => brands.Contains(product.Brand.ToLower());

            if (types.Count > 0)
                criteria = criteria.And(product => types.Contains(product.Type.ToLower()));

            return Task.FromResult(criteria);
        }

        /// <summary>
        /// The query carries paging as flat pageNumber/pageSize (query-string friendly);
        /// this is where it becomes the kernel's PagingInfo, with the bounds applied.
        /// </summary>
        protected override PagingInfo BuildPaging(ProductListQuery query) => new()
        {
            PageNumber = query.PageNumber < 1 ? 1 : query.PageNumber,
            PageRecordCount = query.PageSize <= 0 ? 6 : Math.Min(query.PageSize, 50)
        };

        /// <summary>
        /// Translates the UI's sort key into columns. "priceDesc" is not a column name, so
        /// this is the one place that knows the vocabulary of the catalog's sort control.
        /// </summary>
        protected override IReadOnlyCollection<Common.OrderInfo>? GetOrdering(ProductListQuery query)
            => new List<Common.OrderInfo>
            {
                query.OrderBy switch
                {
                    "price" => new Common.OrderInfo { ColumnName = nameof(ProductEntity.Price), Ascending = true },
                    "priceDesc" => new Common.OrderInfo { ColumnName = nameof(ProductEntity.Price), Ascending = false },
                    _ => new Common.OrderInfo { ColumnName = nameof(ProductEntity.Name), Ascending = true }
                }
            };

        private static List<string> SplitList(string? csv)
            => string.IsNullOrWhiteSpace(csv)
                ? new List<string>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(value => value.ToLowerInvariant())
                     .ToList();
    }

    public class ProductDetailQueryHandler
        : BaseDetailQueryHandler<ProductEntity, ProductDetailQuery,
            DtoReturnOne<ProductDetailDto>, ProductDetailDto>,
          IRequestHandler<ProductDetailQuery, DtoReturnOne<ProductDetailDto>>
    {
        public ProductDetailQueryHandler(
            IProductQueryRepository repository,
            IMapperCustome mapper,
            IAppLogger logger)
            : base(repository, mapper, logger)
        {
        }

        protected override Expression<Func<ProductEntity, object>> SelectExpr => product => new
        {
            product.ProductId,
            product.Name,
            product.Description,
            product.Price,
            product.PictureUrl,
            product.Brand,
            product.Type,
            product.QuantityInStock
        };
    }

    /// <summary>Reads the two distinct lists the catalog's filter controls need.</summary>
    public class ProductFiltersQueryHandler : IRequestHandler<ProductFiltersQuery, DtoReturnOne<ProductFilterDto>>
    {
        private readonly IProductQueryRepository _repository;
        private readonly IAppLogger _logger;

        public ProductFiltersQueryHandler(IProductQueryRepository repository, IAppLogger logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task<DtoReturnOne<ProductFilterDto>> Handle(
            ProductFiltersQuery request,
            CancellationToken cancellationToken)
        {
            var response = await _repository.GetFiltersAsync(cancellationToken);

            if (response.Result is false || response.Data is null)
            {
                var failed = new DtoReturnOne<ProductFilterDto>();
                failed.SetErrorResult(response.Error ?? "The product filters could not be read.");
                return failed;
            }

            var result = new DtoReturnOne<ProductFilterDto>();
            result.SetResultFrom(new List<ProductFilterDto> { response.Data }, 1, 1);
            return result;
        }
    }
}
