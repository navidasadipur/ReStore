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
        if (payload?.Data?.Code != 100 || authority is null || !IsSandboxAuthority(authority))
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

    public async Task<DomainResponseOne<ZarinpalPaymentResultDto>> VerifyAsync(string authority, string status,
        CancellationToken cancellationToken)
    {
        if (settings.Mode != ZarinpalMode.Sandbox)
            return new("ZarinPal verification is currently available in Sandbox only.");
        if (!IsSandboxAuthority(authority) || status is not ("OK" or "NOK"))
            return new("The payment callback is invalid.");

        var matchingAttempt = context.PaymentAttempts.Where(attempt => attempt.IsSandbox && attempt.Authority == authority);
        var attempt = await matchingAttempt.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (attempt is null)
            return new("The payment attempt was not found.");
        if (attempt.Status == PaymentAttemptStatus.Succeeded || status == "NOK")
            return PaymentResult(attempt);
        if (attempt.Status != PaymentAttemptStatus.Pending)
            return new("The payment attempt is no longer pending.");

        using var response = await client.PostAsJsonAsync(new Uri(settings.BaseAddress, "pg/v4/payment/verify.json"), new
        {
            merchant_id = settings.MerchantId,
            amount = attempt.AmountInRials,
            authority
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new("ZarinPal could not verify the payment. Please retry the callback.");

        var verification = (await response.Content.ReadFromJsonAsync<VerifyResponse>(cancellationToken))?.Data;
        if (verification is null || verification.Code is not (100 or 101))
            return new("ZarinPal did not confirm the payment.");
        var reference = verification.RefId is > 0 ? verification.RefId : null;
        if (verification.Code == 100 && reference is null)
            return new("ZarinPal did not return the payment reference. Please retry the callback.");

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var current = await matchingAttempt.SingleAsync(cancellationToken);
        if (current.Status == PaymentAttemptStatus.Succeeded)
        {
            // An overlapping verification can receive 101 before the request with the reference commits.
            if (current.RefId is null && reference.HasValue)
            {
                current.RefId = reference;
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            return PaymentResult(current);
        }
        if (current.Status != PaymentAttemptStatus.Pending)
            return new("The payment attempt is no longer pending.");

        var order = await context.Orders.Include(order => order.OrderItems)
            .SingleAsync(order => order.OrderId == current.OrderId, cancellationToken);
        if (order.CheckoutRequestId is null || order.OrderStatus != OrderStatus.Pending)
            return new("The order is no longer awaiting payment.");

        await DeductStockIfAvailableAsync(order, cancellationToken);
        current.Status = PaymentAttemptStatus.Succeeded;
        current.RefId = reference;
        order.OrderStatus = OrderStatus.PaymentReceived;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PaymentResult(current);
    }

    private async Task DeductStockIfAvailableAsync(OrderEntity order, CancellationToken cancellationToken)
    {
        var quantities = order.OrderItems.GroupBy(item => item.ItemOrdered.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var productIds = quantities.Keys.ToArray();
        var products = await context.Products.Where(product => productIds.Contains(product.ProductId))
            .ToListAsync(cancellationToken);
        if (products.Count != quantities.Count
            || products.Any(product => product.QuantityInStock < quantities[product.ProductId]))
            return;

        foreach (var product in products)
            product.QuantityInStock -= quantities[product.ProductId];
    }

    private static bool IsSandboxAuthority(string? authority)
        => authority is { Length: 36 } && authority[0] == 'S' && authority.All(char.IsAsciiLetterOrDigit);

    private static DomainResponseOne<ZarinpalPaymentResultDto> PaymentResult(PaymentAttemptEntity attempt)
        => new(new ZarinpalPaymentResultDto(attempt.OrderId, attempt.Status == PaymentAttemptStatus.Succeeded, attempt.RefId));

    private DomainResponseOne<ZarinpalPaymentLinkDto> PaymentLink(int orderId, string authority)
        => new(new ZarinpalPaymentLinkDto(orderId, new Uri(settings.BaseAddress, $"pg/StartPay/{authority}")));

    private sealed record PaymentResponse(PaymentData? Data);
    private sealed record PaymentData(int Code, string? Authority);
    private sealed record VerifyResponse(VerifyData? Data);
    private sealed record VerifyData(int Code, [property: System.Text.Json.Serialization.JsonPropertyName("ref_id")] long? RefId);
}
