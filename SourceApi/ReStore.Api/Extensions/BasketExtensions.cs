using ReStore.Api.DTOs;
using ReStore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ReStore.Api.Extensions
{
    public static class BasketExtensions
    {
        public static BasketDto MapBasketToDto(this BasketEntity basket)   
        {
            return new BasketDto
            {
                Id = basket.BasketId,
                BuyerId = basket.BuyerId,
                PaymentIntentId = basket.PaymentIntentId,
                ClientSecret = basket.ClientSecret,
                Items = basket.Items
                .Select(item => new BasketItemDto
                {
                    ProductId = item.ProductId,
                    Name = item.Product.Name,
                    Price = item.Product.Price,
                    PictureUrl = item.Product.PictureUrl,
                    Type = item.Product.Type,
                    Brand = item.Product.Brand,
                    Quantity = item.Quantity
                }).ToList()
            };
        }

        public static IQueryable<BasketEntity> RetrieveBasketWithItems(this IQueryable<BasketEntity> query, string buyerId)
        {
            return query.Include(i => i.Items)
                .ThenInclude(p => p.Product).Where(b => b.BuyerId == buyerId);
        }
    }
}