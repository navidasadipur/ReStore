using System.Collections.Generic;
using System.Linq;
using ReStore.Domain.Entities;

namespace ReStore.Application.DTOs
{
    /// <summary>
    /// The basket as the client sees it. Built by an explicit factory rather than Mapster:
    /// each line's display fields come from the related product, which a name-based map
    /// would silently get wrong the moment a property is renamed.
    /// </summary>
    public class BasketDto : BaseDto
    {
        public int Id { get; set; }

        public string BuyerId { get; set; } = string.Empty;

        public List<BasketItemDto> Items { get; set; } = new();



        public static BasketDto FromEntity(BasketEntity basket) => new()
        {
            Id = basket.BasketId,
            BuyerId = basket.BuyerId,
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
                })
                .ToList()
        };
    }

    public class BasketItemDto : BaseDto
    {
        public int ProductId { get; set; }

        public string Name { get; set; } = string.Empty;

        public long Price { get; set; }

        public string? PictureUrl { get; set; }

        public string Type { get; set; } = string.Empty;

        public string Brand { get; set; } = string.Empty;

        public int Quantity { get; set; }
    }
}
