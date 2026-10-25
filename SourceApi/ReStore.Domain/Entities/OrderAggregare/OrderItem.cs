using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReStore.Domain.Common;

namespace ReStore.Domain.Entities.OrderAggregare
{
    public class OrderItem : BaseEntity
    {
        public int Id { get; set; }
        public ProductItemOrdered ItemOrdered { get; set; } 
        public long Price { get; set; }
        public int Quantity { get; set; }
    }
}