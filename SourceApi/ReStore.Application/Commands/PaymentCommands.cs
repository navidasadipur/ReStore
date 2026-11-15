using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;

namespace ReStore.Application.Commands;

public class CreateOrUpdatePaymentIntentCommand : IRequest<DtoReturnOne<BasketDto>>;

public class ProcessPaymentWebhookCommand : IRequest<UpdateReturn>
{
    public string Payload { get; set; } = string.Empty;
    public string? Signature { get; set; }
}

public sealed record StartZarinpalPaymentCommand(int OrderId) : IRequest<DtoReturnOne<ZarinpalPaymentLinkDto>>;
