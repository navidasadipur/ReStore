using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IOrderRepository
{
    Task<DomainResponse> CreatePendingFromBasketAsync(string buyerId, Guid checkoutRequestId,
        ShippingAddress? shippingAddress, bool saveAddress, CancellationToken cancellationToken = default);
}
