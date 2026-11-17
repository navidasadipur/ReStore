using System.Threading;
using System.Threading.Tasks;
using MediatR;
using ReStore.Application.Commands;
using ReStore.Application.DTOs;
using ReStore.Application.Handlers.QueryHandlers;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;

namespace ReStore.Application.Handlers.CommandHandlers
{
    /// <summary>
    /// Adds a product to the basket.
    ///
    /// Deliberately not one of the base CRUD handlers: the rule ("the same product again
    /// increments its line") belongs to the BasketEntity aggregate, and the repository
    /// applies it to tracked instances. The handler's job is the buyer, the guard, and the
    /// response shape.
    /// </summary>
    public class AddBasketItemCommandHandler
        : IRequestHandler<AddBasketItemCommand, DtoReturnOne<BasketDto>>
    {
        private readonly IBasketRepository _baskets;
        private readonly IBuyerIdentity _buyerIdentity;

        public AddBasketItemCommandHandler(IBasketRepository baskets, IBuyerIdentity buyerIdentity)
        {
            _baskets = baskets;
            _buyerIdentity = buyerIdentity;
        }

        public async Task<DtoReturnOne<BasketDto>> Handle(
            AddBasketItemCommand request,
            CancellationToken cancellationToken)
        {
            if (request.Quantity <= 0)
                return BasketResults.Failure("Quantity must be at least 1.");

            // A first-time visitor gets an anonymous id here, and the cookie is written by
            // the implementation so the basket is theirs on the next request.
            var buyerId = _buyerIdentity.BuyerId ?? _buyerIdentity.CreateAnonymousBuyerId();

            var response = await _baskets.AddItemAsync(
                buyerId, request.ProductId, request.Quantity, cancellationToken);

            return response is { Result: true, Data: BasketEntity basket }
                ? BasketResults.Success(basket)
                : BasketResults.Failure(response.Error);
        }
    }

    /// <summary>Removes a quantity of a product from the basket.</summary>
    public class RemoveBasketItemCommandHandler
        : IRequestHandler<RemoveBasketItemCommand, DtoReturnOne<BasketDto>>
    {
        private readonly IBasketRepository _baskets;
        private readonly IBuyerIdentity _buyerIdentity;

        public RemoveBasketItemCommandHandler(IBasketRepository baskets, IBuyerIdentity buyerIdentity)
        {
            _baskets = baskets;
            _buyerIdentity = buyerIdentity;
        }

        public async Task<DtoReturnOne<BasketDto>> Handle(
            RemoveBasketItemCommand request,
            CancellationToken cancellationToken)
        {
            if (request.Quantity <= 0)
                return BasketResults.Failure("Quantity must be at least 1.");

            var buyerId = _buyerIdentity.BuyerId;

            // Nothing of theirs to remove: the same answer as an empty basket.
            if (string.IsNullOrWhiteSpace(buyerId))
                return BasketResults.Empty();

            var response = await _baskets.RemoveItemAsync(
                buyerId, request.ProductId, request.Quantity, cancellationToken);

            if (response.Result is false)
                return BasketResults.Failure(response.Error);

            // No basket existed, so there was nothing to remove: same answer as an empty one.
            return response.Data is BasketEntity basket
                ? BasketResults.Success(basket)
                : BasketResults.Empty();
        }
    }
}
