using ReStore.Domain.Entities.OrderAggregare;

namespace ReStore.Api.DTOs
{
    public class CreateOrderDto
    {
        public bool SaveAddress { get; set; }
        public ShippingAddress ShippingAddress { get; set; }        
    }
}