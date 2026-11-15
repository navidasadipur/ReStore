using MediatR;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;

namespace ReStore.Application.Handlers.CommandHandlers;

public sealed class VerifyZarinpalPaymentCommandHandler(IZarinpalPaymentService payments, IAppLogger logger)
    : IRequestHandler<VerifyZarinpalPaymentCommand, DtoReturnOne<ZarinpalPaymentResultDto>>
{
    public async Task<DtoReturnOne<ZarinpalPaymentResultDto>> Handle(VerifyZarinpalPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var result = new DtoReturnOne<ZarinpalPaymentResultDto>();
        try
        {
            var payment = await payments.VerifyAsync(request.Authority, request.Status, cancellationToken);
            if (payment.Result && payment.Data is not null)
                result.SetResultFrom([payment.Data], 1, 1);
            else
                result.SetErrorResult(payment.Error ?? "The payment could not be verified.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, nameof(VerifyZarinpalPaymentCommandHandler));
            result.SetErrorResult("The payment could not be verified. Please retry the callback.");
        }
        return result;
    }
}
