using Microsoft.EntityFrameworkCore;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Repositories
{
    /// <summary>
    /// Basket reads and writes.
    ///
    /// The two mutations load *tracked* instances — the product via FindAsync and the
    /// basket with its items and their products — then let the aggregate's own AddItem /
    /// RemoveItem decide what happens and save once. Loading the product untracked (as a
    /// query repository would) and handing it to a tracked basket makes EF treat it as a
    /// new product, which is the bug this shape avoids.
    /// </summary>
    public sealed class BasketRepository : IBasketRepository
    {
        private readonly StoreContext _context;

        public BasketRepository(StoreContext context) => _context = context;

        public async Task<DomainResponseOne<BasketEntity>> GetWithItemsAsync(
            string buyerId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var basket = await _context.Baskets
                    .AsNoTracking()
                    .Include(basket => basket.Items)
                    .ThenInclude(item => item.Product)
                    .SingleOrDefaultAsync(basket => basket.BuyerId == buyerId, cancellationToken);

                return basket is null
                    ? new DomainResponseOne<BasketEntity>()
                    : new DomainResponseOne<BasketEntity>(basket);
            }
            catch (Exception exception)
            {
                return new DomainResponseOne<BasketEntity>(exception);
            }
        }

        public async Task<DomainResponse> AddItemAsync(
            string buyerId,
            int productId,
            int quantity,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var product = await _context.Products
                    .SingleOrDefaultAsync(product => product.ProductId == productId, cancellationToken);

                if (product is null)
                    return new DomainResponse($"No product with id {productId} was found.");

                var basket = await GetOrCreateTrackedAsync(buyerId, cancellationToken);

                basket.AddItem(product, quantity);

                await _context.SaveChangesAsync(cancellationToken);

                return new DomainResponse(basket);
            }
            catch (Exception exception)
            {
                _context.ChangeTracker.Clear();
                return new DomainResponse(exception);
            }
        }

        public async Task<DomainResponse> RemoveItemAsync(
            string buyerId,
            int productId,
            int quantity,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Deliberately does not create a basket: removing from a basket that does
                // not exist is a no-op, and creating an empty row for it would leave junk
                // behind every time a stale client asks to remove something.
                var basket = await _context.Baskets
                    .Include(basket => basket.Items)
                    .ThenInclude(item => item.Product)
                    .SingleOrDefaultAsync(basket => basket.BuyerId == buyerId, cancellationToken);

                if (basket is null)
                    return new DomainResponse(data: null);

                basket.RemoveItem(productId, quantity);

                await _context.SaveChangesAsync(cancellationToken);

                return new DomainResponse(basket);
            }
            catch (Exception exception)
            {
                _context.ChangeTracker.Clear();
                return new DomainResponse(exception);
            }
        }

        /// <summary>
        /// The basket for this buyer, tracked with items and their products, created in
        /// memory when absent (the caller's SaveChanges persists it).
        /// </summary>
        private async Task<BasketEntity> GetOrCreateTrackedAsync(
            string buyerId,
            CancellationToken cancellationToken)
        {
            var basket = await _context.Baskets
                .Include(basket => basket.Items)
                .ThenInclude(item => item.Product)
                .SingleOrDefaultAsync(basket => basket.BuyerId == buyerId, cancellationToken);

            if (basket is not null)
                return basket;

            basket = new BasketEntity { BuyerId = buyerId };
            _context.Baskets.Add(basket);

            return basket;
        }
    }
}
