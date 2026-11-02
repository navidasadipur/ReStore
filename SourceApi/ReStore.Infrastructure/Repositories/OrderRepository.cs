using System.Data;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.Controls;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Common;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories;

public sealed class OrderRepository(StoreContext context, OrderControls controls) : IOrderRepository
{
    public async Task<DomainResponse> CreateFromBasketAsync(string buyerId, ShippingAddress? shippingAddress,
        bool saveAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            // Serializable prevents competing checkouts from committing based on the
            // same basket or stale stock. A conflicting request fails and may be retried
            // by the caller; we never replay a checkout automatically.
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var basket = await context.Baskets.Include(b => b.Items).ThenInclude(i => i.Product)
                .SingleOrDefaultAsync(b => b.BuyerId == buyerId, cancellationToken);

            var basketCheck = controls.CheckBasket(basket);
            if (!basketCheck.Result)
                return basketCheck;

            var items = basket!.Items.Select(item => new OrderItemEntity
            {
                ItemOrdered = new ProductItemOrdered
                {
                    ProductId = item.ProductId,
                    Name = item.Product.Name,
                    PictureUrl = item.Product.PictureUrl ?? string.Empty
                },
                Price = item.Product.Price,
                Quantity = item.Quantity
            }).ToList();

            var subtotal = checked(items.Sum(item => checked(item.Price * item.Quantity)));
            var order = new OrderEntity
            {
                BuyerId = buyerId,
                ShippingAddress = shippingAddress is null ? null! : CopyAddress<ShippingAddress>(shippingAddress),
                OrderItems = items,
                SubTotal = subtotal,
                DeliveryFee = StoreMoney.GetDeliveryFee(subtotal),
                PaymentIntentId = basket.PaymentIntentId!
            };

            var orderCheck = await controls.CheckOnAdd(order);
            if (!orderCheck.Result)
                return orderCheck;

            if (saveAddress)
            {
                var user = await context.Users.Include(u => u.Address)
                    .SingleOrDefaultAsync(u => u.UserName == buyerId, cancellationToken);
                if (user is null)
                    return new DomainResponse("The buyer account could not be found.");

                // Update the existing address to preserve its key and relationship.
                var address = user.Address ?? new UserAddress();
                CopyAddress(order.ShippingAddress, address);
                user.Address = address;
            }

            foreach (var item in basket.Items)
                item.Product.QuantityInStock -= item.Quantity;

            context.Orders.Add(order);
            context.Baskets.Remove(basket);

            // No generic repository calls here: their ChangeTracker.Clear would
            // detach the other pending writes in this checkout.
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DomainResponse(order.OrderId);
        }
        catch (Exception exception)
        {
            // Disposal of the transaction rolls back a failed save or commit.
            return new DomainResponse(exception);
        }
        finally
        {
            context.ChangeTracker.Clear();
        }
    }

    private static T CopyAddress<T>(Address source) where T : Address, new()
    {
        var target = new T();
        CopyAddress(source, target);
        return target;
    }

    private static void CopyAddress(Address source, Address target)
    {
        target.FullName = source.FullName;
        target.Address1 = source.Address1;
        target.Address2 = source.Address2;
        target.City = source.City;
        target.State = source.State;
        target.Zip = source.Zip;
        target.Country = source.Country;
    }
}
