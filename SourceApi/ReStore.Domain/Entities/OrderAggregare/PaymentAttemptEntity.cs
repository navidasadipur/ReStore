namespace ReStore.Domain.Entities.OrderAggregare;

public enum PaymentAttemptStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3
}

public sealed class PaymentAttemptEntity
{
    public long Id { get; set; }
    public int OrderId { get; set; }
    public long AmountInRials { get; set; }
    public bool IsSandbox { get; set; }
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public string? Authority { get; set; }
    public long? RefId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
