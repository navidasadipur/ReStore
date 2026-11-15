using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IOrderRepository
{
    /// <summary>Creates the order, adjusts stock, removes the basket and optionally saves the address atomically.</summary>
    Task<DomainResponse> CreateFromBasketAsync(string buyerId, ShippingAddress? shippingAddress,
        bool saveAddress, CancellationToken cancellationToken = default);

    Task<DomainResponse> CreatePendingFromBasketAsync(string buyerId, Guid checkoutRequestId,
        ShippingAddress? shippingAddress, bool saveAddress, CancellationToken cancellationToken = default);
}
