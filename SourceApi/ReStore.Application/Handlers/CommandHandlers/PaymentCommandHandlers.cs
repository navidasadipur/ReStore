using MediatR;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.Controls;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;

namespace ReStore.Application.Handlers.CommandHandlers;

public class CreateOrUpdatePaymentIntentCommandHandler(IBasketRepository baskets,
    IPaymentRepository payments, IPaymentGateway gateway, PaymentControls controls,
    IBuyerIdentity buyer, IAppLogger logger)
    : IRequestHandler<CreateOrUpdatePaymentIntentCommand, DtoReturnOne<BasketDto>>
{
    public async Task<DtoReturnOne<BasketDto>> Handle(CreateOrUpdatePaymentIntentCommand request, CancellationToken cancellationToken)
    {
        if (!buyer.IsAuthenticated || string.IsNullOrWhiteSpace(buyer.BuyerId))
            return Failure("An authenticated buyer is required.");

        try
        {
            var read = await baskets.GetWithItemsAsync(buyer.BuyerId, cancellationToken);
            if (!read.Result)
            {
                if (read.Exception is not null)
                    logger.LogError(read.Exception, nameof(CreateOrUpdatePaymentIntentCommandHandler));
                return Failure(read.Error);
            }

            var check = controls.CheckForPayment(read.Data);
            if (!check.Result)
                return Failure(check.Error);

            var basket = read.Data!;
            var amount = controls.GetAmount(basket);
            var intentId = string.IsNullOrWhiteSpace(basket.PaymentIntentId) ? null : basket.PaymentIntentId;
            var key = $"restore-basket-{basket.BasketId}-intent-{intentId ?? "new"}-amount-{amount}";
            var payment = await gateway.CreateOrUpdateAsync(amount, intentId, key, cancellationToken);
            if (!payment.Result || payment.Data is null)
            {
                if (payment.Exception is not null)
                    logger.LogError(payment.Exception, nameof(CreateOrUpdatePaymentIntentCommandHandler));
                return Failure(payment.Error);
            }

            var saved = await payments.SaveIntentAsync(basket.BasketId, buyer.BuyerId, basket.PaymentIntentId,
                payment.Data, cancellationToken);
            if (!saved.Result)
            {
                if (saved.Exception is not null)
                    logger.LogError(saved.Exception, nameof(CreateOrUpdatePaymentIntentCommandHandler));
                return Failure(saved.Error);
            }

            basket.PaymentIntentId = payment.Data.Id;
            basket.ClientSecret = payment.Data.ClientSecret;
            var result = new DtoReturnOne<BasketDto>();
            result.SetResultFrom(new List<BasketDto> { BasketDto.FromEntity(basket) }, 1, 1);
            return result;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(CreateOrUpdatePaymentIntentCommandHandler));
            return Failure(exception.Message);
        }
    }

    private static DtoReturnOne<BasketDto> Failure(string? error)
    {
        var result = new DtoReturnOne<BasketDto>();
        result.SetErrorResult(error ?? "The payment intent could not be created or updated.");
        return result;
    }
}

public class ProcessPaymentWebhookCommandHandler(IPaymentGateway gateway, IPaymentRepository payments, IAppLogger logger)
    : IRequestHandler<ProcessPaymentWebhookCommand, UpdateReturn>
{
    public async Task<UpdateReturn> Handle(ProcessPaymentWebhookCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var parsed = gateway.ReadWebhook(request.Payload, request.Signature);
            if (!parsed.Result || parsed.Data is null)
            {
                if (parsed.Exception is not null)
                    logger.LogError(parsed.Exception, nameof(ProcessPaymentWebhookCommandHandler));
                return new UpdateReturn((parsed.Error ?? "The webhook could not be verified.", parsed.ErrorCode ?? string.Empty));
            }

            var notification = parsed.Data;
            if (notification.Status is null || string.IsNullOrWhiteSpace(notification.PaymentIntentId))
                return new UpdateReturn();

            var response = await payments.ApplyOrderStatusAsync(notification.PaymentIntentId, notification.Status.Value, cancellationToken);
            if (response.Exception is not null)
                logger.LogError(response.Exception, nameof(ProcessPaymentWebhookCommandHandler));
            return new UpdateReturn(response);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, nameof(ProcessPaymentWebhookCommandHandler));
            return new UpdateReturn(exception);
        }
    }
}
