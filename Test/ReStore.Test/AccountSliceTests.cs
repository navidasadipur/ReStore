using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using ReStore.Application.Commands;
using ReStore.Application.Handlers.CommandHandlers;
using ReStore.Application.Handlers.QueryHandlers;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;
using ReStore.Infrastructure.Repositories;
using ReStore.Infrastructure.Services;
using Xunit;

namespace ReStore.Test;

public sealed class AccountSliceTests : IDisposable
{
    private const string Password = "Pa$$w0rd!";
    private const string TokenKey = "restore-account-test-key-0123456789-abcdefghijklmnopqrstuvwxyz-0123456789";
    private readonly SqliteStore _store = new();
    private readonly ServiceProvider _services;
    private readonly IIdentityService _identity;
    private readonly ITokenGenerator _tokens;

    public AccountSliceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_store.Context);
        services.AddIdentityCore<User>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<Role>().AddEntityFrameworkStores<StoreContext>();
        _services = services.BuildServiceProvider();
        _identity = new IdentityService(_services.GetRequiredService<UserManager<User>>(), _store.Context);
        _tokens = new JwtTokenGenerator(Mock.Of<IConfiguration>(configuration => configuration["JWTSettings:TokenKey"] == TokenKey));
    }

    private async Task<int> Register(string username = "Alice", string email = "alice@example.test")
    {
        var result = await new RegisterCommandHandler(_identity, new NoopAppLogger()).Handle(
            new RegisterCommand { Username = username, Email = email, Password = Password }, CancellationToken.None);
        result.Succeeded.Should().BeTrue(result.Error);
        return result.Key;
    }

    private LoginCommandHandler Login(FakeBuyerIdentity buyer, IAccountBasketRepository? baskets = null, ITokenGenerator? tokens = null)
        => new(_identity, tokens ?? _tokens, baskets ?? new AccountBasketRepository(_store.Context), buyer, new NoopAppLogger());

    private CurrentUserQueryHandler Current(FakeBuyerIdentity buyer, IBasketRepository? baskets = null)
        => new(_identity, _tokens, baskets ?? new BasketRepository(_store.Context), buyer, new NoopAppLogger());

    private async Task<int> SeedBasket(string buyer, string name)
    {
        var product = _store.SeedProduct(name: name);
        _store.Context.Attach(product);
        var basket = new BasketEntity { BuyerId = buyer, PaymentIntentId = "pi-guest", ClientSecret = "secret-guest" };
        basket.AddItem(product, 2);
        _store.Context.Baskets.Add(basket);
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();
        return basket.BasketId;
    }

    [Fact]
    public async Task Registration_creates_a_hashed_password_and_member_role_and_login_issues_a_valid_token()
    {
        var key = await Register();
        var manager = _services.GetRequiredService<UserManager<User>>();
        var user = await manager.FindByIdAsync(key.ToString());
        user.Should().NotBeNull();
        user!.PasswordHash.Should().NotBe(Password);
        (await manager.CheckPasswordAsync(user, Password)).Should().BeTrue();
        (await manager.IsInRoleAsync(user, "Member")).Should().BeTrue();

        var result = await Login(new FakeBuyerIdentity()).Handle(
            new LoginCommand { Username = "aLiCe", Password = Password }, CancellationToken.None);
        result.Succeeded.Should().BeTrue(result.Error);
        result.Data!.Email.Should().Be("alice@example.test");
        result.Data.Basket.Should().BeNull();
        var principal = new JwtSecurityTokenHandler().ValidateToken(result.Data.Token, new TokenValidationParameters
        {
            ValidateIssuer = false, ValidateAudience = false, ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenKey)),
            ClockSkew = TimeSpan.Zero
        }, out var token);
        principal.Identity!.Name.Should().Be("Alice");
        principal.IsInRole("Member").Should().BeTrue();
        principal.FindFirst(ClaimTypes.Email)!.Value.Should().Be("alice@example.test");
        token.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Registration_validation_errors_leave_no_partial_user()
    {
        await Register();
        var duplicate = await _identity.RegisterAsync("Other", "alice@example.test", Password, CancellationToken.None);
        var weak = await _identity.RegisterAsync("Weak", "weak@example.test", "123", CancellationToken.None);
        duplicate.Result.Should().BeFalse();
        duplicate.ErrorCode.Should().Contain("DuplicateEmail");
        weak.Result.Should().BeFalse();
        (await _store.Context.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Failure_assigning_the_member_role_rolls_back_user_creation()
    {
        var role = await _store.Context.Roles.SingleAsync(r => r.Name == "Member");
        _store.Context.Roles.Remove(role);
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();

        var result = await new RegisterCommandHandler(_identity, new NoopAppLogger()).Handle(
            new RegisterCommand { Username = "Alice", Email = "alice@example.test", Password = Password }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        (await _store.Context.Users.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("Alice", "WrongPassword!")]
    [InlineData("Unknown", Password)]
    public async Task Invalid_credentials_do_not_transfer_a_basket_or_clear_the_cookie(string username, string password)
    {
        await Register();
        var guest = Guid.NewGuid().ToString();
        var basketId = await SeedBasket(guest, "Guest item");
        var buyer = new FakeBuyerIdentity { BuyerId = guest, AnonymousBuyerId = guest };

        var result = await Login(buyer).Handle(new LoginCommand { Username = username, Password = password }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Invalid username or password.");
        buyer.CookieCleared.Should().BeFalse();
        (await _store.Context.Baskets.AsNoTracking().SingleAsync(b => b.BasketId == basketId)).BuyerId.Should().Be(guest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_transfers_the_guest_basket_using_the_canonical_username(bool existingAccountBasket)
    {
        await Register();
        if (existingAccountBasket)
            await SeedBasket("Alice", "Old account item");
        var guest = Guid.NewGuid().ToString();
        var basketId = await SeedBasket(guest, "Guest item");
        var buyer = new FakeBuyerIdentity { BuyerId = guest, AnonymousBuyerId = guest };

        var result = await Login(buyer).Handle(new LoginCommand { Username = "ALICE", Password = Password }, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Data!.Basket!.Id.Should().Be(basketId);
        result.Data.Basket.BuyerId.Should().Be("Alice");
        result.Data.Basket.Items.Should().ContainSingle().Which.Name.Should().Be("Guest item");
        buyer.CookieCleared.Should().BeTrue();
        (await _store.Context.Baskets.CountAsync()).Should().Be(1);
        (await _store.Context.Baskets.SingleAsync()).BuyerId.Should().Be("Alice");
    }

    [Fact]
    public async Task A_cookie_containing_an_account_name_cannot_take_over_its_basket()
    {
        await Register();
        var aliceBasket = await SeedBasket("Alice", "Alice item");
        var otherBasket = await SeedBasket("OtherAccount", "Other item");
        var buyer = new FakeBuyerIdentity { AnonymousBuyerId = "OtherAccount" };

        var result = await Login(buyer).Handle(new LoginCommand { Username = "Alice", Password = Password }, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Data!.Basket!.Id.Should().Be(aliceBasket);
        (await _store.Context.Baskets.AsNoTracking().SingleAsync(b => b.BasketId == otherBasket)).BuyerId.Should().Be("OtherAccount");
    }

    [Fact]
    public async Task A_transfer_failure_after_save_rolls_back_both_baskets_and_keeps_the_cookie()
    {
        await Register();
        var accountBasket = await SeedBasket("Alice", "Account item");
        var guest = Guid.NewGuid().ToString();
        var guestBasket = await SeedBasket(guest, "Guest item");
        var buyer = new FakeBuyerIdentity { AnonymousBuyerId = guest };
        var options = new DbContextOptionsBuilder<StoreContext>()
            .UseSqlite(_store.Context.Database.GetDbConnection()).AddInterceptors(new FailAfterSave()).Options;
        await using var failingContext = new StoreContext(options);

        var result = await Login(buyer, new AccountBasketRepository(failingContext)).Handle(
            new LoginCommand { Username = "Alice", Password = Password }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        buyer.CookieCleared.Should().BeFalse();
        _store.Context.ChangeTracker.Clear();
        (await _store.Context.Baskets.AsNoTracking().SingleAsync(b => b.BasketId == accountBasket)).BuyerId.Should().Be("Alice");
        (await _store.Context.Baskets.AsNoTracking().SingleAsync(b => b.BasketId == guestBasket)).BuyerId.Should().Be(guest);
        (await _store.Context.Baskets.AsNoTracking().Include(b => b.Items).SingleAsync(b => b.BasketId == accountBasket)).Items.Should().ContainSingle();
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Guest transfer failed after save");
    }

    [Fact]
    public async Task Token_failure_does_not_transfer_the_basket_or_clear_the_cookie()
    {
        await Register();
        var guest = Guid.NewGuid().ToString();
        var basketId = await SeedBasket(guest, "Guest item");
        var buyer = new FakeBuyerIdentity { AnonymousBuyerId = guest };
        var tokens = new Mock<ITokenGenerator>();
        tokens.Setup(t => t.GenerateToken(It.IsAny<User>(), It.IsAny<IReadOnlyCollection<string>>()))
            .Throws(new InvalidOperationException("Signing failed"));

        var result = await Login(buyer, tokens: tokens.Object).Handle(
            new LoginCommand { Username = "Alice", Password = Password }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        buyer.CookieCleared.Should().BeFalse();
        (await _store.Context.Baskets.AsNoTracking().SingleAsync(b => b.BasketId == basketId)).BuyerId.Should().Be(guest);
    }

    [Fact]
    public async Task Current_user_and_saved_address_return_only_the_authenticated_account()
    {
        await Register();
        await SeedBasket("Alice", "Alice item");
        var buyer = new FakeBuyerIdentity().AsAuthenticated("Alice");
        var current = await Current(buyer).Handle(new CurrentUserQuery(), CancellationToken.None);
        current.Succeeded.Should().BeTrue(current.Error);
        current.Data!.Basket!.BuyerId.Should().Be("Alice");
        current.Data.Token.Should().NotBeNullOrWhiteSpace();

        var handler = new SavedAddressQueryHandler(_identity, buyer, new NoopAppLogger());
        var empty = await handler.Handle(new SavedAddressQuery(), CancellationToken.None);
        empty.Succeeded.Should().BeTrue(empty.Error);
        empty.Data.Should().BeNull();

        var user = await _store.Context.Users.SingleAsync(u => u.UserName == "Alice");
        user.Address = new UserAddress
        {
            FullName = "Alice", Address1 = "Street", Address2 = "", City = "City",
            State = "State", Zip = "12345", Country = "Country"
        };
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();
        var address = await handler.Handle(new SavedAddressQuery(), CancellationToken.None);
        address.Succeeded.Should().BeTrue(address.Error);
        address.Data!.FullName.Should().Be("Alice");

        var anonymous = new FakeBuyerIdentity { BuyerId = "Alice" };
        (await Current(anonymous).Handle(new CurrentUserQuery(), CancellationToken.None)).Succeeded.Should().BeFalse();
        (await new SavedAddressQueryHandler(_identity, anonymous, new NoopAppLogger())
            .Handle(new SavedAddressQuery(), CancellationToken.None)).Succeeded.Should().BeFalse();
        (await Current(new FakeBuyerIdentity().AsAuthenticated("DeletedAccount"))
            .Handle(new CurrentUserQuery(), CancellationToken.None)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_basket_read_error_is_not_hidden_by_current_user()
    {
        await Register();
        var baskets = new Mock<IBasketRepository>();
        baskets.Setup(b => b.GetWithItemsAsync("Alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponseOne<BasketEntity>(new InvalidOperationException("Basket read failed")));
        var result = await Current(new FakeBuyerIdentity().AsAuthenticated("Alice"), baskets.Object)
            .Handle(new CurrentUserQuery(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Basket read failed");
    }

    public void Dispose()
    {
        _services.Dispose();
        _store.Dispose();
    }
}
