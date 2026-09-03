using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace API.Entities.OrderAggregare
{
    public class Order
    {
        public int Id { get; set; }
        public string BuyerId { get; set; }
        public ShippingAddress ShippingAddress { get; set; }
        // Must be UTC: this column is "timestamp with time zone", and Npgsql
        // refuses to write a DateTime whose Kind is Local or Unspecified.
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public List<OrderItem> OrderItems { get; set; }
        public long SubTotal { get; set; }
        public long DeliveryFee { get; set; }
        public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;
        public string PaymentIntentId { get; set; }

        public long GetTotal()
        {
            return SubTotal + DeliveryFee;
        }
    }
}