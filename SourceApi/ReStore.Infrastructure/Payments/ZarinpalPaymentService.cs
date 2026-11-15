using System.Data;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.DTOs;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Payments;

public sealed class ZarinpalPaymentService(HttpClient client, ZarinpalSettings settings, StoreContext context)
    : IZarinpalPaymentService
{
    private const string UnavailableOrder = "A pending order was not found for this buyer.";

    public async Task<DomainResponseOne<ZarinpalPaymentLinkDto>> StartAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        if (settings.Mode != ZarinpalMode.Sandbox)
            return new("ZarinPal payment links are currently available in Sandbox only.");

        var eligibleOrder = context.Orders.AsNoTracking().Where(order => order.OrderId == orderId
            && order.BuyerId == buyerId && order.CheckoutRequestId != null && order.OrderStatus == OrderStatus.Pending);
        var order = await eligibleOrder.SingleOrDefaultAsync(cancellationToken);
        if (order is null)
            return new(UnavailableOrder);

        var amount = checked(order.SubTotal + order.DeliveryFee);
        if (amount <= 0)
            return new("The order amount must be positive.");

        var pendingAttempts = context.PaymentAttempts.AsNoTracking().Where(attempt => attempt.OrderId == orderId
            && attempt.IsSandbox && attempt.Status == PaymentAttemptStatus.Pending && attempt.Authority != null)
            .OrderByDescending(attempt => attempt.Id).Select(attempt => attempt.Authority);
        var existing = await pendingAttempts.FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return PaymentLink(orderId, existing);

        using var response = await client.PostAsJsonAsync(new Uri(settings.BaseAddress, "pg/v4/payment/request.json"), new
        {
            merchant_id = settings.MerchantId,
            amount,
            currency = "IRR",
            callback_url = settings.CallbackUrl.AbsoluteUri,
            description = $"ReStore order #{orderId}"
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return new("ZarinPal could not start the payment.");

        var payload = await response.Content.ReadFromJsonAsync<PaymentResponse>(cancellationToken);
        var authority = payload?.Data?.Authority;
        if (payload?.Data?.Code != 100 || authority is not { Length: 36 } || authority[0] != 'S'
            || !authority.All(char.IsAsciiLetterOrDigit))
            return new("ZarinPal did not return a valid Sandbox payment link.");

        // Only publish links saved locally. A competing request can reuse the
        // committed link, or retry if PostgreSQL rejects its serializable write.
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await eligibleOrder.AnyAsync(cancellationToken))
            return new(UnavailableOrder);
        existing = await pendingAttempts.FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
            return PaymentLink(orderId, existing);

        context.PaymentAttempts.Add(new PaymentAttemptEntity
        {
            OrderId = orderId, AmountInRials = amount, IsSandbox = true, Authority = authority
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentLink(orderId, authority);
    }

    private DomainResponseOne<ZarinpalPaymentLinkDto> PaymentLink(int orderId, string authority)
        => new(new ZarinpalPaymentLinkDto(orderId, new Uri(settings.BaseAddress, $"pg/StartPay/{authority}")));

    private sealed record PaymentResponse(PaymentData? Data);
    private sealed record PaymentData(int Code, string? Authority);
}
