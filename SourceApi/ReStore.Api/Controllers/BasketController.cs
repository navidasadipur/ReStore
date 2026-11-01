using MediatR;
using ReStore.Api.Filters;
using Microsoft.AspNetCore.Mvc;
using ReStore.Application.Commands;
using ReStore.Application.DTOs;
using ReStore.Application.Queries;

namespace ReStore.Api.Controllers
{
    /// <summary>
    /// The basket, in the same shape as Products: bind, one Send, return the envelope.
    ///
    /// The buyer is no longer worked out here — <c>IBuyerIdentity</c> answers that, and the
    /// cookie is written by its implementation, so nothing in this controller touches
    /// HttpContext or Identity any more.
    /// </summary>
    [EnvelopeResponse]
    public class BasketController : BaseApiController
    {
        private readonly IMediator _mediator;

        public BasketController(IMediator mediator) => _mediator = mediator;

        [HttpGet(Name = "GetBasket")]
        public async Task<DtoReturnOne<BasketDto>> GetBasket(CancellationToken cancellationToken)
            => await _mediator.Send(new BasketQuery(), cancellationToken);

        [HttpPost]
        public async Task<DtoReturnOne<BasketDto>> AddItemToBasket(
            int productId,
            int quantity,
            CancellationToken cancellationToken)
            => await _mediator.Send(
                new AddBasketItemCommand { ProductId = productId, Quantity = quantity },
                cancellationToken);

        [HttpDelete]
        public async Task<DtoReturnOne<BasketDto>> RemoveBasketItem(
            int productId,
            int quantity,
            CancellationToken cancellationToken)
            => await _mediator.Send(
                new RemoveBasketItemCommand { ProductId = productId, Quantity = quantity },
                cancellationToken);
    }
}
