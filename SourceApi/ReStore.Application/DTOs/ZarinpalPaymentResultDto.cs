namespace ReStore.Application.DTOs;

public sealed class ZarinpalPaymentResultDto(int orderId, bool paid, long? referenceId) : BaseDto
{
    public int OrderId { get; } = orderId;
    public bool Paid { get; } = paid;
    public long? ReferenceId { get; } = referenceId;
}
