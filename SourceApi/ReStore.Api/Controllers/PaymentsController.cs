using MediatR;
using ReStore.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;

namespace ReStore.Api.Controllers;

public class PaymentsController(IMediator mediator) : BaseApiController
{
    [Authorize]
    [EnvelopeResponse]
    [HttpPost("zarinpal/{orderId:int}")]
    public async Task<DtoReturnOne<ZarinpalPaymentLinkDto>> StartZarinpalPayment(int orderId,
        CancellationToken cancellationToken)
        => await mediator.Send(new StartZarinpalPaymentCommand(orderId), cancellationToken);

    [AllowAnonymous]
    [EnvelopeResponse]
    [HttpGet("zarinpal/callback")]
    public async Task<DtoReturnOne<ZarinpalPaymentResultDto>> ZarinpalCallback(
        [FromQuery] string authority, [FromQuery] string status, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return await mediator.Send(new VerifyZarinpalPaymentCommand(authority, status), cancellationToken);
    }
}
