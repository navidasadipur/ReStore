using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ReStore.Application.Commands;
using ReStore.Application.Handlers.CommandHandlers;
using ReStore.Application.Handlers.QueryHandlers;
using ReStore.Application.Interfaces;
using ReStore.Application.Queries;
using ReStore.Infrastructure.Persistence;
using ReStore.Infrastructure.Repositories;
using Xunit;

namespace ReStore.Test;

/// <summary>
/// Stands in for the HTTP buyer: no cookies, no HttpContext, just the two facts the
/// handlers need. The real one lives in the Api project.
/// </summary>
public sealed class FakeBuyerIdentity : IBuyerIdentity
{
    public string? BuyerId { get; set; }

    public string? AnonymousBuyerId { get; set; }

    public bool IsAuthenticated { get; private set; }

    public int AnonymousIdsCreated { get; private set; }

    public bool CookieCleared { get; private set; }

    public string CreateAnonymousBuyerId()
    {
        AnonymousIdsCreated++;
        BuyerId = $"anonymous-{AnonymousIdsCreated}-{Guid.NewGuid():N}";
        AnonymousBuyerId = BuyerId;
        return BuyerId;
    }

    public void ClearAnonymousBuyerId()
    {
        CookieCleared = true;
        AnonymousBuyerId = null;
        if (!IsAuthenticated)
            BuyerId = null;
    }

    public FakeBuyerIdentity AsAuthenticated(string userName)
    {
        BuyerId = userName;
        IsAuthenticated = true;
        return this;
    }
}

public class BasketSliceTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;

    public BasketSliceTests(SqliteStore store) => _store = store;

    private BasketRepository Repository() => new(_store.Context);

    private AddBasketItemCommandHandler AddHandler(IBuyerIdentity buyer)
        => new(Repository(), buyer);

    private RemoveBasketItemCommandHandler RemoveHandler(IBuyerIdentity buyer)
        => new(Repository(), buyer);

    private BasketQueryHandler QueryHandler(IBuyerIdentity buyer)
        => new(Repository(), buyer, new NoopAppLogger());

    [Fact]
    public async Task A_visitor_without_a_basket_gets_an_empty_success()
    {
        var buyer = new FakeBuyerIdentity { BuyerId = "no-basket-buyer" };

        var response = await Repository().GetWithItemsAsync(buyer.BuyerId);
        response.Result.Should().BeTrue();
        response.Data.Should().BeNull();

        var result = await QueryHandler(buyer).Handle(new BasketQuery(), CancellationToken.None);

        result.Result.Should().BeTrue("an empty basket is not an error");
        result.Data.Should().BeNull();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task A_database_read_failure_is_not_reported_as_an_empty_success()
    {
        // A separate database without a schema makes the real EF read fail without
        // damaging the shared fixture or requiring an external database server.
        var options = new DbContextOptionsBuilder<StoreContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        await using var context = new StoreContext(options);
        var repository = new BasketRepository(context);
        var buyer = new FakeBuyerIdentity { BuyerId = "database-failure-buyer" };
        var handler = new BasketQueryHandler(repository, buyer, new NoopAppLogger());

        var result = await handler.Handle(new BasketQuery(), CancellationToken.None);

        result.Result.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task Adding_to_a_new_visitor_creates_the_basket_and_an_anonymous_id()
    {
        var buyer = new FakeBuyerIdentity();
        var product = _store.SeedProduct(name: "Basket add", price: 300, brand: "BasketBrand");

        var result = await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = product.ProductId, Quantity = 2 },
            CancellationToken.None);

        result.Result.Should().BeTrue();
        buyer.AnonymousIdsCreated.Should().Be(1, "the cookie is created with the first basket");
        buyer.BuyerId.Should().NotBeNull();

        result.Data!.BuyerId.Should().Be(buyer.BuyerId);
        result.Data.Items.Should().ContainSingle();
        result.Data.Items[0].ProductId.Should().Be(product.ProductId);
        result.Data.Items[0].Quantity.Should().Be(2);
        result.Data.Items[0].Name.Should().Be("Basket add");
        result.Data.Items[0].Price.Should().Be(300);
    }

    [Fact]
    public async Task Adding_the_same_product_twice_increments_one_line()
    {
        var buyer = new FakeBuyerIdentity { BuyerId = "repeat-buyer" };
        var product = _store.SeedProduct(name: "Basket repeat", brand: "RepeatBrand");

        await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = product.ProductId, Quantity = 1 }, CancellationToken.None);

        var second = await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = product.ProductId, Quantity = 3 }, CancellationToken.None);

        second.Data!.Items.Should().ContainSingle("the aggregate merges the lines");
        second.Data.Items[0].Quantity.Should().Be(4);
    }

    [Fact]
    public async Task Adding_an_unknown_product_fails_and_creates_no_basket()
    {
        var buyer = new FakeBuyerIdentity { BuyerId = "unknown-product-buyer" };

        var result = await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = 987654, Quantity = 1 }, CancellationToken.None);

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("987654");

        var basket = await QueryHandler(buyer).Handle(new BasketQuery(), CancellationToken.None);
        basket.Data.Should().BeNull();
    }

    [Fact]
    public async Task Adding_a_zero_quantity_is_rejected()
    {
        var buyer = new FakeBuyerIdentity { BuyerId = "zero-buyer" };
        var product = _store.SeedProduct(name: "Zero", brand: "ZeroBrand");

        var result = await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = product.ProductId, Quantity = 0 }, CancellationToken.None);

        result.Result.Should().BeFalse();
        result.Error.Should().Contain("at least 1");
    }

    [Fact]
    public async Task Removing_reduces_the_quantity_and_then_clears_the_line()
    {
        var buyer = new FakeBuyerIdentity { BuyerId = "remove-buyer" };
        var product = _store.SeedProduct(name: "Remove me", brand: "RemoveBrand");

        await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = product.ProductId, Quantity = 3 }, CancellationToken.None);

        var partial = await RemoveHandler(buyer).Handle(
            new RemoveBasketItemCommand { ProductId = product.ProductId, Quantity = 1 }, CancellationToken.None);

        partial.Data!.Items.Should().ContainSingle();
        partial.Data.Items[0].Quantity.Should().Be(2);

        var cleared = await RemoveHandler(buyer).Handle(
            new RemoveBasketItemCommand { ProductId = product.ProductId, Quantity = 5 }, CancellationToken.None);

        cleared.Data!.Items.Should().BeEmpty("removing more than present empties the line");
    }

    [Fact]
    public async Task A_signed_in_buyer_uses_their_user_name_not_a_cookie()
    {
        var buyer = new FakeBuyerIdentity().AsAuthenticated("alice");
        var product = _store.SeedProduct(name: "Alice item", brand: "AliceBrand");

        await AddHandler(buyer).Handle(
            new AddBasketItemCommand { ProductId = product.ProductId, Quantity = 1 }, CancellationToken.None);

        buyer.AnonymousIdsCreated.Should().Be(0, "authenticated visitors never get an anonymous id");

        _store.Context.ChangeTracker.Clear();
        var saved = await _store.Context.Baskets
            .AsNoTracking()
            .Include(basket => basket.Items)
            .SingleAsync(basket => basket.BuyerId == "alice");

        saved.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Removing_without_a_basket_answers_empty_rather_than_failing()
    {
        var buyer = new FakeBuyerIdentity { BuyerId = "nothing-to-remove" };

        var result = await RemoveHandler(buyer).Handle(
            new RemoveBasketItemCommand { ProductId = 1, Quantity = 1 }, CancellationToken.None);

        result.Result.Should().BeTrue();
        result.Data.Should().BeNull();
    }
}
