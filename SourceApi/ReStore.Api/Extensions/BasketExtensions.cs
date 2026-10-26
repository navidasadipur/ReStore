using ReStore.Application.DTOs;
using ReStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ReStore.Api.Extensions
{
    public static class BasketExtensions
    {
        /// <summary>
        /// Kept for the controllers that still read StoreContext directly (account, orders,
        /// payments) until their slices land. The mapping itself has one home now:
        /// BasketDto.FromEntity in Application.
        /// </summary>
        public static BasketDto MapBasketToDto(this BasketEntity basket)
            => BasketDto.FromEntity(basket);

        public static IQueryable<BasketEntity> RetrieveBasketWithItems(this IQueryable<BasketEntity> query, string buyerId)
        {
            return query.Include(i => i.Items)
                .ThenInclude(p => p.Product).Where(b => b.BuyerId == buyerId);
        }
    }
}