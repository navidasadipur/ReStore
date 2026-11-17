using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IAccountBasketRepository
{
    /// <summary>Preserves the legacy login rule: an existing guest basket replaces the account basket atomically.</summary>
    Task<DomainResponseOne<BasketEntity>> TransferGuestBasketAsync(
        string username, string? anonymousBuyerId, CancellationToken cancellationToken);
}
