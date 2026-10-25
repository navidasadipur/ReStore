using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReStore.Api.DTOs;
using ReStore.Domain.Entities.OrderAggregare;
using Microsoft.EntityFrameworkCore;

namespace ReStore.Api.Extensions
{
    public static class OrderExtensions
    {
        public static IQueryable<OrderDto> ProjectOrderToOrderDto(this IQueryable<OrderEntity> query)
        {
            return query
                .Select(order => new OrderDto
                {
                    Id = order.OrderId,
                    BuyerId = order.BuyerId,
                    OrderDate = order.OrderDate,
                    ShippingAddress = order.ShippingAddress,
                    DeliveryFee = order.DeliveryFee,
                    SubTotal = order.SubTotal,
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
                }).AsNoTracking();
        }
    }
}