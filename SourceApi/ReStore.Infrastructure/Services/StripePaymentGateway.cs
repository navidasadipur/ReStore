using Microsoft.Extensions.Configuration;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using Stripe;

namespace ReStore.Infrastructure.Services;

public sealed class StripePaymentGateway(IConfiguration configuration, IStripeClient? client = null) : IPaymentGateway
{
    public async Task<DomainResponseOne<PaymentIntentInfo>> CreateOrUpdateAsync(long amount, string? intentId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var secret = configuration["StripeSettings:SecretKey"];
            if (client is null && string.IsNullOrWhiteSpace(secret))
                return new DomainResponseOne<PaymentIntentInfo>("Stripe is not configured.");

            var service = new PaymentIntentService(client ?? new StripeClient(secret));
            var requestOptions = new RequestOptions { IdempotencyKey = idempotencyKey };
            var intent = string.IsNullOrWhiteSpace(intentId)
                ? await service.CreateAsync(new PaymentIntentCreateOptions
                {
                    Amount = amount, Currency = "usd", PaymentMethodTypes = new List<string> { "card" }
                }, requestOptions, cancellationToken)
                : await service.UpdateAsync(intentId, new PaymentIntentUpdateOptions { Amount = amount }, requestOptions, cancellationToken);

            if (string.IsNullOrWhiteSpace(intent.Id) || string.IsNullOrWhiteSpace(intent.ClientSecret))
                return new DomainResponseOne<PaymentIntentInfo>("Stripe returned an incomplete payment intent.");
            return new DomainResponseOne<PaymentIntentInfo>(new PaymentIntentInfo(intent.Id, intent.ClientSecret));
        }
        catch (Exception exception) { return new DomainResponseOne<PaymentIntentInfo>(exception); }
    }

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
