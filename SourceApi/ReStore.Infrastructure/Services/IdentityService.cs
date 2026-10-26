using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;

namespace ReStore.Infrastructure.Services;

public sealed class IdentityService(UserManager<User> users, StoreContext context) : IIdentityService
{
    public async Task<DomainResponseOne<User>> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = await users.FindByNameAsync(username);
            return user is not null && await users.CheckPasswordAsync(user, password)
                ? new DomainResponseOne<User>(user)
                : new DomainResponseOne<User>("Invalid username or password.");
        }
        catch (Exception exception) { return new DomainResponseOne<User>(exception); }
    }

    public async Task<DomainResponse> RegisterAsync(string username, string email, string password, CancellationToken cancellationToken)
    {
        try
        {
            // UserManager saves when creating a user and again when assigning a role.
            // Both saves must succeed, or neither may remain in the database.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var user = new User { UserName = username, Email = email };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                return Failed(created);

            var role = await users.AddToRoleAsync(user, "Member");
            if (!role.Succeeded)
                return Failed(role);

            await transaction.CommitAsync(cancellationToken);
            return new DomainResponse(user.Id);
        }
        catch (Exception exception) { return new DomainResponse(exception); }
        finally { context.ChangeTracker.Clear(); }
    }

    public async Task<DomainResponseOne<User>> FindByNameAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = await users.FindByNameAsync(username);
            return user is null ? new DomainResponseOne<User>("The account could not be found.") : new DomainResponseOne<User>(user);
        }
        catch (Exception exception) { return new DomainResponseOne<User>(exception); }
    }

    public async Task<IReadOnlyCollection<string>> GetRolesAsync(User user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return (await users.GetRolesAsync(user)).ToArray();
    }

    public async Task<DomainResponseOne<UserAddress>> GetAddressAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var normalized = users.NormalizeName(username);
            var user = await context.Users.AsNoTracking().Include(u => u.Address)
                .SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken);
            if (user is null)
                return new DomainResponseOne<UserAddress>("The account could not be found.");
            return user.Address is null ? new DomainResponseOne<UserAddress>() : new DomainResponseOne<UserAddress>(user.Address);
        }
        catch (Exception exception) { return new DomainResponseOne<UserAddress>(exception); }
    }

    private static DomainResponse Failed(IdentityResult result)
        => new(string.Join(Environment.NewLine, result.Errors.Select(e => e.Description)),
            string.Join(",", result.Errors.Select(e => e.Code)));
}
