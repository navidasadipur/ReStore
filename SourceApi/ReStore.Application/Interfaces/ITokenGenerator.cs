using ReStore.Domain.Entities;

namespace ReStore.Application.Interfaces;

public interface ITokenGenerator
{
    string GenerateToken(User user, IReadOnlyCollection<string> roles);
}
