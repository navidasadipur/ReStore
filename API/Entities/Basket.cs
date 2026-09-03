using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace API.Entities
{
    public class Basket
    {
        public int Id { get; set; }
        public string BuyerId { get; set; }
        public List<BasketItem> Items { get; set; } = new();
        public string? PaymentIntentId { get; set; }
        public string? ClientSecret { get; set; }

        public void AddItem(Product product, int quantity)
        {
            if (quantity <= 0) return;

            var existingItem = Items.FirstOrDefault(item => item.ProductId == product.Id);

            if (existingItem == null)
            {
                Items.Add(new BasketItem { Product = product, ProductId = product.Id, Quantity = quantity });
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