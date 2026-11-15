using MediatR;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;

namespace ReStore.Application.Handlers.CommandHandlers;

public class CreateOrderCommandHandler(IOrderRepository repository, IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<CreateOrderCommand, InsertReturn>
{
    public async Task<InsertReturn> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (!buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId))
            return new InsertReturn(("An authenticated buyer is required.", "Unauthorized"));

        var response = await repository.CreateFromBasketAsync(
            buyer.BuyerId, request.ShippingAddress, request.SaveAddress, cancellationToken);

        if (response.Exception is not null)
            logger.LogError(response.Exception, nameof(CreateOrderCommandHandler));

        return response.Result
            ? new InsertReturn(response.LastKey, response)
            : new InsertReturn((response.Error ?? "The order could not be created.", response.ErrorCode ?? string.Empty));
    }
}

public sealed class CreatePendingOrderCommandHandler(IOrderRepository repository, IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<CreatePendingOrderCommand, InsertReturn>
{
    public async Task<InsertReturn> Handle(CreatePendingOrderCommand request, CancellationToken cancellationToken)
    {
        if (!buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId))
            return new InsertReturn(("An authenticated buyer is required.", "Unauthorized"));
        if (request.CheckoutRequestId == Guid.Empty)
            return new InsertReturn(("A non-empty checkout request ID is required.", "InvalidCheckoutRequestId"));

        var response = await repository.CreatePendingFromBasketAsync(buyer.BuyerId, request.CheckoutRequestId,
            request.ShippingAddress, request.SaveAddress, cancellationToken);

        if (response.Exception is not null)
            logger.LogError(response.Exception, nameof(CreatePendingOrderCommandHandler));
        return response.Result
            ? new InsertReturn(response.LastKey, response)
            : new InsertReturn((response.Error ?? "The order could not be created.", response.ErrorCode ?? string.Empty));
    }
}
