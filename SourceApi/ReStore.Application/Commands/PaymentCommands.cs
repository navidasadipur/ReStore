using MediatR;
using ReStore.Application.Common;
using ReStore.Application.DTOs;

namespace ReStore.Application.Commands;

public sealed record StartZarinpalPaymentCommand(int OrderId) : IRequest<DtoReturnOne<ZarinpalPaymentLinkDto>>;

public sealed record VerifyZarinpalPaymentCommand(string Authority, string Status)
    : IRequest<DtoReturnOne<ZarinpalPaymentResultDto>>;
