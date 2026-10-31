using ReStore.Domain.Entities.OrderAggregare;

namespace ReStore.Application.Common;

public sealed record PaymentIntentInfo(string Id, string ClientSecret);

// Null status means a verified event that does not change an order.
public sealed record PaymentNotification(string? PaymentIntentId, OrderStatus? Status);
