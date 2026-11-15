using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities.OrderAggregare
{
    public class OrderEntity : BaseEntity
    {
        /// <summary>Key is "OrderId", not Id/OrderEntityId, so it needs [Key].</summary>
        [Key]
        public int OrderId { get; set; }
        public string BuyerId { get; set; }
        public ShippingAddress ShippingAddress { get; set; }
        // Must be UTC: this column is "timestamp with time zone", and Npgsql
        // refuses to write a DateTime whose Kind is Local or Unspecified.
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public List<OrderItemEntity> OrderItems { get; set; }
        public long SubTotal { get; set; }
        public long DeliveryFee { get; set; }
        public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;
        public string? PaymentIntentId { get; set; }
        public Guid? CheckoutRequestId { get; set; }

        public long GetTotal()
        {
            return SubTotal + DeliveryFee;
        }
    }
}
