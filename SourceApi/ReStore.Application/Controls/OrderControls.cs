using ReStore.Domain.Entities;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using ReStore.Domain.Interfaces.Repository;

namespace ReStore.Application.Controls;

public class OrderControls(IGenericQueryRepositoryBase<OrderEntity> repository)
    : BaseControls<OrderEntity>(repository)
{
    // Checked inside the checkout transaction, before any stock is changed.
    public DomainResponse CheckBasket(BasketEntity? basket)
    {
        if (basket is null || basket.Items.Count == 0)
            return new DomainResponse("The basket is empty.");

        foreach (var item in basket.Items)
        {
            if (item.Product is null)
                return new DomainResponse("A product in your basket is no longer available.");
            if (item.Quantity <= 0)
                return new DomainResponse("Quantity must be at least 1.");
            if (item.Product.QuantityInStock < item.Quantity)
                return new DomainResponse($"Not enough {item.Product.Name} in stock.");
        }

        return new DomainResponse();
    }

    protected override Task<DomainResponse> CheckOnAddInternal(OrderEntity entity)
        => Task.FromResult(entity.ShippingAddress is null
            ? new DomainResponse("Shipping address is required.")
            : new DomainResponse());
}
