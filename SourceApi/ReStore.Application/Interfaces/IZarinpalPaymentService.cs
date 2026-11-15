using ReStore.Application.DTOs;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IZarinpalPaymentService
{
    Task<DomainResponseOne<ZarinpalPaymentLinkDto>> StartAsync(string buyerId, int orderId,
        CancellationToken cancellationToken);

    Task<DomainResponseOne<ZarinpalPaymentResultDto>> VerifyAsync(string authority, string status,
        CancellationToken cancellationToken);
}
