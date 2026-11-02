namespace ReStore.Domain.Common;

/// <summary>Catalog and order amounts are integer Iranian rials.</summary>
public static class StoreMoney
{
    public const long DeliveryFee = 500_000;
    public const long FreeDeliveryThreshold = 10_000_000;

    public static long GetDeliveryFee(long subtotal)
        => subtotal > FreeDeliveryThreshold ? 0 : DeliveryFee;
}
