using Microsoft.EntityFrameworkCore;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories;

public sealed class PaymentRepository(StoreContext context) : IPaymentRepository
{
    public async Task<DomainResponse> SaveIntentAsync(int basketId, string buyerId, string? expectedIntentId,
        PaymentIntentInfo intent, CancellationToken cancellationToken)
    {
        try
        {
            // Update only the payment columns, without attaching the product graph.
            // The buyer/key guard rejects a basket removed or transferred in flight.
            var saved = await context.Baskets.Where(b => b.BasketId == basketId && b.BuyerId == buyerId
                    && (b.PaymentIntentId == expectedIntentId || b.PaymentIntentId == intent.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(b => b.PaymentIntentId, intent.Id)
                    .SetProperty(b => b.ClientSecret, intent.ClientSecret), cancellationToken);
            return saved == 1 ? new DomainResponse() : new DomainResponse("The basket changed before its payment intent could be saved.");
        }
        catch (Exception exception) { return new DomainResponse(exception); }
    }

    public async Task<DomainResponse> ApplyOrderStatusAsync(string intentId, OrderStatus status, CancellationToken cancellationToken)
    {
        try
        {
            // Repeated deliveries are no-ops. A late failure must not undo a
            // payment success; success may still supersede an earlier failure.
            await context.Orders.Where(order => order.PaymentIntentId == intentId && order.OrderStatus != status
                    && (status != OrderStatus.PaymentFailed || order.OrderStatus != OrderStatus.PaymentReceived))
                .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.OrderStatus, status), cancellationToken);
            return new DomainResponse();
        }
        catch (Exception exception) { return new DomainResponse(exception); }
    }
}
