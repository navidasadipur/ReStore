using MediatR;
using ReStore.Application.Common;
using ReStore.Domain.Entities.OrderAggregare;

namespace ReStore.Application.Commands;

public class CreateOrderCommand : BaseAddCommand, IRequest<InsertReturn>
{
    public bool SaveAddress { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }
}
