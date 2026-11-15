using MediatR;
using ReStore.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Queries;

namespace ReStore.Api.Controllers;

[Authorize]
[EnvelopeResponse]
public class OrdersController(IMediator mediator) : BaseApiController
{
    [HttpGet]
    public async Task<PaginatedResponseListDto<OrderDto>> GetOrders(
        [FromQuery] OrderListQuery query, CancellationToken cancellationToken)
        => await mediator.Send(query, cancellationToken);

    [HttpGet("{id:int}", Name = "GetOrder")]
    public async Task<DtoReturnOne<OrderDto>> GetOrder(
        [FromRoute] OrderDetailQuery query, CancellationToken cancellationToken)
        => await mediator.Send(query, cancellationToken);

    [HttpPost("pending")]
    public async Task<InsertReturn> CreatePendingOrder(
        [FromBody] CreatePendingOrderCommand command, CancellationToken cancellationToken)
        => await mediator.Send(command, cancellationToken);

    [HttpPost]
    public async Task<InsertReturn> CreateOrder(
        [FromBody] CreateOrderCommand command, CancellationToken cancellationToken)
        => await mediator.Send(command, cancellationToken);
}
