using ReStore.Domain.Entities.OrderAggregare;

namespace ReStore.Application.DTOs;

public class OrderDto : BaseDto
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public ShippingAddress ShippingAddress { get; set; } = new();
    public DateTime OrderDate { get; set; }
    public List<OrderItemDto> OrderItems { get; set; } = new();
    public long SubTotal { get; set; }
    public long DeliveryFee { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public long Total { get; set; }

    public static OrderDto FromEntity(OrderEntity order) => new()
    {
        Id = order.OrderId,
        BuyerId = order.BuyerId,
        ShippingAddress = order.ShippingAddress,
        OrderDate = order.OrderDate,
        SubTotal = order.SubTotal,
        DeliveryFee = order.DeliveryFee,
        OrderStatus = order.OrderStatus.ToString(),
        Total = order.GetTotal(),
        OrderItems = order.OrderItems.Select(item => new OrderItemDto
        {
            ProductId = item.ItemOrdered.ProductId,
            Name = item.ItemOrdered.Name,
            PictureUrl = item.ItemOrdered.PictureUrl,
            Price = item.Price,
            Quantity = item.Quantity
        }).ToList()
    };
}

public class OrderItemDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PictureUrl { get; set; }
    public long Price { get; set; }
    public int Quantity { get; set; }
}
