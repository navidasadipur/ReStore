using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;

namespace ReStore.Application.Queries
{
    /// <summary>
    /// The catalog list — bound straight from the query string, so the action method
    /// takes this type as its parameter instead of a transport DTO that would have to be
    /// copied into it.
    /// </summary>
    public class ProductListQuery : BaseQuery, IRequest<PaginatedResponseListDto<ProductListDto>>
    {
        /// <summary>Comma-separated brands, matched case-insensitively. Empty means all.</summary>
        public string? Brands { get; set; }

        /// <summary>Comma-separated types, matched case-insensitively. Empty means all.</summary>
        public string? Types { get; set; }

        /// <summary>
        /// The UI's sort key rather than a column name: "price", "priceDesc", or
        /// anything else for name order. Translated to columns by the handler.
        /// </summary>
        public string? OrderBy { get; set; }

        /// <summary>1-based page. Flat, not nested in PagingInfo, so ?pageNumber= keeps working.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Rows per page; the handler clamps it.</summary>
        public int PageSize { get; set; } = 6;
    }

    public class ProductDetailQuery : BaseDetailQuery, IRequest<DtoReturnOne<ProductDetailDto>>
    {
    }

    /// <summary>Distinct brands and types, for the catalog's filter controls.</summary>
    public class ProductFiltersQuery : IRequest<DtoReturnOne<ProductFilterDto>>
    {
    }
}
