using MediatR;
using ReStore.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReStore.Api.DTOs;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Queries;
using ReStore.Domain.Entities;

namespace ReStore.Api.Controllers
{
    /// <summary>
    /// Products, as thin as it gets: bind, send one query or command, return the envelope.
    /// No EF, no mapping, no business rules — those live in Application.
    ///
    /// The query and the delete command are the action parameters themselves. The two
    /// commands that carry a file are the exception, and for a concrete reason: see the
    /// comment on <c>CreateProduct</c>.
    ///
    /// Responses are the envelope itself with HTTP 200 (decision D1): a failure is
    /// Result=Failed with a message, not a status code.
    /// </summary>
    [EnvelopeResponse]
    public class ProductsController : BaseApiController
    {
        /// <summary>
        /// The columns a product form may change. Written out rather than "all columns"
        /// so the partial-update path stays explicit, and so the caller cannot choose the
        /// columns by sending a different ChangeFields list.
        /// </summary>
        private static readonly string[] EditableColumns =
        [
            nameof(ProductEntity.Name),
            nameof(ProductEntity.Description),
            nameof(ProductEntity.Price),
            nameof(ProductEntity.Type),
            nameof(ProductEntity.Brand),
            nameof(ProductEntity.QuantityInStock)
        ];

        private readonly IMediator _mediator;

        public ProductsController(IMediator mediator) => _mediator = mediator;

        [HttpGet]
        public async Task<PaginatedResponseListDto<ProductListDto>> GetProducts(
            [FromQuery] ProductListQuery query,
            CancellationToken cancellationToken)
        {
            return await _mediator.Send(query, cancellationToken);
        }

        [HttpGet("{id:int}", Name = "GetProduct")]
        public async Task<DtoReturnOne<ProductDetailDto>> GetProduct(
            [FromRoute] ProductDetailQuery query,
            CancellationToken cancellationToken)
            => await _mediator.Send(query, cancellationToken);

        [HttpGet("filters")]
        public async Task<DtoReturnOne<ProductFilterDto>> GetFilters(CancellationToken cancellationToken)
            => await _mediator.Send(new ProductFiltersQuery(), cancellationToken);

        /// <summary>
        /// Create takes a form DTO rather than the command, and this is the one real
        /// reason for a transport type here: the file arrives as IFormFile, and the
        /// command carries a Stream so that ReStore.Application needs no reference to
        /// ASP.NET Core (plan §7.6). The DTO is the adapter that keeps IFormFile on this
        /// side of the boundary.
        /// </summary>
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<InsertReturn> CreateProduct(
            [FromForm] CreateProductDto productDto,
            CancellationToken cancellationToken)
        {
            var command = productDto.ToCommand();
            return await _mediator.Send(command, cancellationToken);
        }

        /// <summary>Same file boundary as <see cref="CreateProduct"/>.</summary>
        [Authorize(Roles = "Admin")]
        [HttpPut]
        public async Task<UpdateReturn> UpdateProduct(
            [FromForm] UpdateProductDto productDto,
            CancellationToken cancellationToken)
        {
            var command = productDto.ToCommand(EditableColumns);
            return await _mediator.Send(command, cancellationToken);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id:int}")]
        public async Task<DeleteReturn> DeleteProduct(
            [FromRoute] DeleteProductCommand command,
            CancellationToken cancellationToken)
            => await _mediator.Send(command, cancellationToken);
    }
}
