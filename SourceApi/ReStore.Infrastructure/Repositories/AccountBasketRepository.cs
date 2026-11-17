using System.Data;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories;

public sealed class AccountBasketRepository(StoreContext context) : IAccountBasketRepository
{
    public async Task<DomainResponseOne<BasketEntity>> TransferGuestBasketAsync(
        string username, string? anonymousBuyerId, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var userBasket = await WithItems().SingleOrDefaultAsync(b => b.BuyerId == username, cancellationToken);

            // Only generated guest IDs are eligible; a forged cookie containing
            // another account's username must never transfer that account's basket.
            var guest = Guid.TryParse(anonymousBuyerId, out _) && anonymousBuyerId != username
                ? await WithItems().SingleOrDefaultAsync(b => b.BuyerId == anonymousBuyerId, cancellationToken)
                : null;

            if (guest is not null)
            {
                if (userBasket is not null)
                    context.Baskets.Remove(userBasket);
                guest.BuyerId = username;
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            var basket = guest ?? userBasket;
            return basket is null ? new DomainResponseOne<BasketEntity>() : new DomainResponseOne<BasketEntity>(basket);
        }
        catch (Exception exception) { return new DomainResponseOne<BasketEntity>(exception); }
        finally { context.ChangeTracker.Clear(); }
    }

    private IQueryable<BasketEntity> WithItems()
        => context.Baskets.Include(b => b.Items).ThenInclude(i => i.Product);
}
