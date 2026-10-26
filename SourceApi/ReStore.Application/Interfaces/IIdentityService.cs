using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;

namespace ReStore.Application.Interfaces;

public interface IIdentityService
{
    Task<DomainResponseOne<User>> AuthenticateAsync(string username, string password, CancellationToken cancellationToken);
    Task<DomainResponse> RegisterAsync(string username, string email, string password, CancellationToken cancellationToken);
    Task<DomainResponseOne<User>> FindByNameAsync(string username, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetRolesAsync(User user, CancellationToken cancellationToken);
    Task<DomainResponseOne<UserAddress>> GetAddressAsync(string username, CancellationToken cancellationToken);
}
