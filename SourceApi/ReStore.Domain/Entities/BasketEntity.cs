using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities
{
    public class BasketEntity : BaseEntity
    {
        /// <summary>Key is "BasketId", not Id/BasketEntityId, so it needs [Key].</summary>
        [Key]
        public int BasketId { get; set; }
        public string BuyerId { get; set; }
        public List<BasketItemEntity> Items { get; set; } = new();
        public string? PaymentIntentId { get; set; }
        public string? ClientSecret { get; set; }

        public void AddItem(ProductEntity product, int quantity)
        {
            if (quantity <= 0) return;

            var existingItem = Items.FirstOrDefault(item => item.ProductId == product.ProductId);

            if (existingItem == null)
            {
                Items.Add(new BasketItemEntity { Product = product, ProductId = product.ProductId, Quantity = quantity });
                return;
            }

            existingItem.Quantity += quantity;
        }

        public void RemoveItem(int productId, int quantity)
        {
            if (quantity <= 0) return;

            var item = Items.FirstOrDefault(item => item.ProductId == productId);
            if (item == null) return;

            item.Quantity -= quantity;

            // Removing more than the basket holds must empty the line, not
            // leave it sitting there with a negative quantity.
            if (item.Quantity <= 0) Items.Remove(item);
        }
    }
}
