using ReStore.Application.Common;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IPaymentRepository
{
    Task<DomainResponse> SaveIntentAsync(int basketId, string buyerId, string? expectedIntentId,
        PaymentIntentInfo intent, CancellationToken cancellationToken);

    Task<DomainResponse> ApplyOrderStatusAsync(string intentId, OrderStatus status, CancellationToken cancellationToken);
}
