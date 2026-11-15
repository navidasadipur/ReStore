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
    [HttpPost]
    public async Task<DtoReturnOne<BasketDto>> CreateOrUpdatePaymentIntent(CancellationToken cancellationToken)
        => await mediator.Send(new CreateOrUpdatePaymentIntentCommand(), cancellationToken);

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

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<ActionResult> StripeWebhook(CancellationToken cancellationToken)
    {
        // Stripe signatures cover the raw body, so this endpoint cannot bind a JSON DTO.
        var payload = await new StreamReader(Request.Body).ReadToEndAsync(cancellationToken);
        var result = await mediator.Send(new ProcessPaymentWebhookCommand
        {
            Payload = payload,
            Signature = Request.Headers["Stripe-Signature"].ToString()
        }, cancellationToken);

        if (result.Succeeded)
            return new EmptyResult();
        // Webhooks use provider acknowledgements: a storage failure must remain
        // retryable instead of being acknowledged as an HTTP 200 envelope.
        return result.ErrorCode == "InvalidWebhook" ? BadRequest() : StatusCode(StatusCodes.Status500InternalServerError);
    }
}
