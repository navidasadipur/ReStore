using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReStore.Domain.Entities.OrderAggregare
{
    /// <summary>
    /// Owned by <see cref="Order"/>. The ownership is declared in
    /// StoreContext.OnModelCreating rather than with EF's [Owned] attribute, so
    /// this project stays free of any persistence framework.
    /// </summary>
    public class ShippingAddress : Address
    {
        
    }
}