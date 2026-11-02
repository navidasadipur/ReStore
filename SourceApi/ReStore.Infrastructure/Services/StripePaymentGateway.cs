using Microsoft.Extensions.Configuration;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using Stripe;

namespace ReStore.Infrastructure.Services;

public sealed class StripePaymentGateway(IConfiguration configuration, IStripeClient? client = null) : IPaymentGateway
{
    // Retain signed webhooks for historical Stripe orders. Never send rial amounts as USD.
    public Task<DomainResponseOne<PaymentIntentInfo>> CreateOrUpdateAsync(long amount, string? intentId,
        string idempotencyKey, CancellationToken cancellationToken)
        => Task.FromResult(new DomainResponseOne<PaymentIntentInfo>(
            "Payments are temporarily unavailable while we switch to ZarinPal. No payment has been taken."));

    public DomainResponseOne<PaymentNotification> ReadWebhook(string payload, string? signature)
    {
        var secret = configuration["StripeSettings:WhSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return new DomainResponseOne<PaymentNotification>("Stripe webhook signing secret is not configured.");

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, secret);
            if (stripeEvent.Data.Object is not Charge charge)
                return new DomainResponseOne<PaymentNotification>(new PaymentNotification(null, null));

            var status = charge.Status switch
            {
                "succeeded" => (OrderStatus?)OrderStatus.PaymentReceived,
                "failed" => OrderStatus.PaymentFailed,
                _ => null
            };
            return new DomainResponseOne<PaymentNotification>(new PaymentNotification(charge.PaymentIntentId, status));
        }
        catch (Exception exception) when (exception is StripeException or Newtonsoft.Json.JsonException or ArgumentException)
        {
            return new DomainResponseOne<PaymentNotification>("Invalid Stripe webhook signature or payload.") { ErrorCode = "InvalidWebhook" };
        }
    }
}
