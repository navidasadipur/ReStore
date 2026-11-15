using MediatR;
using ReStore.Application.Common;
using ReStore.Domain.Entities.OrderAggregare;

namespace ReStore.Application.Commands;

public class CreateOrderCommand : BaseAddCommand, IRequest<InsertReturn>
{
    public bool SaveAddress { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }
}

public sealed class CreatePendingOrderCommand : IRequest<InsertReturn>
{
    public Guid CheckoutRequestId { get; set; }
    public bool SaveAddress { get; set; }
    public ShippingAddress? ShippingAddress { get; set; }
}
