namespace ReStore.Application.DTOs;

public sealed class ZarinpalPaymentLinkDto(int orderId, Uri paymentUrl) : BaseDto
{
    public int OrderId { get; } = orderId;
    public Uri PaymentUrl { get; } = paymentUrl;
}
