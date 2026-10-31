using ReStore.Application.Common;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IPaymentGateway
{
    Task<DomainResponseOne<PaymentIntentInfo>> CreateOrUpdateAsync(long amount, string? intentId,
        string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Verifies the signature before translating the provider event.</summary>
    DomainResponseOne<PaymentNotification> ReadWebhook(string payload, string? signature);
}
