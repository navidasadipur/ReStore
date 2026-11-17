using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities.OrderAggregare
{
    public class OrderItemEntity : BaseEntity
    {
        /// <summary>Key is "OrderItemId", not Id/OrderItemEntityId, so it needs [Key].</summary>
        [Key]
        public int OrderItemId { get; set; }
        public ProductItemOrdered ItemOrdered { get; set; } 
        public long Price { get; set; }
        public int Quantity { get; set; }
    }
}
