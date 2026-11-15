using MediatR;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;

namespace ReStore.Application.Handlers.CommandHandlers;

public sealed class StartZarinpalPaymentCommandHandler(IZarinpalPaymentService payments, IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<StartZarinpalPaymentCommand, DtoReturnOne<ZarinpalPaymentLinkDto>>
{
    public async Task<DtoReturnOne<ZarinpalPaymentLinkDto>> Handle(StartZarinpalPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var result = new DtoReturnOne<ZarinpalPaymentLinkDto>();
        if (!buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId))
        {
            result.SetErrorResult("An authenticated buyer is required.");
            return result;
        }

        try
        {
            var payment = await payments.StartAsync(buyer.BuyerId, request.OrderId, cancellationToken);
            if (payment.Result && payment.Data is not null)
                result.SetResultFrom([payment.Data], 1, 1);
            else
                result.SetErrorResult(payment.Error ?? "The payment could not be started.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, nameof(StartZarinpalPaymentCommandHandler));
            result.SetErrorResult("The payment could not be started. Please try again.");
        }

        return result;
    }
}
