using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.Controls;
using ReStore.Application.Handlers.CommandHandlers;
using ReStore.Application.Handlers.QueryHandlers;
using ReStore.Application.Queries;
using ReStore.Domain.Entities;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Infrastructure.Persistence;
using ReStore.Infrastructure.Repositories;
using Xunit;

namespace ReStore.Test;

public class OrderSliceTests : IClassFixture<SqliteStore>
{
    private readonly SqliteStore _store;
    public OrderSliceTests(SqliteStore store) => _store = store;

    private GenericQueryRepositoryBase<OrderEntity> Reads(StoreContext? context = null)
        => new(context ?? _store.Context, _store.Mapper);

    private OrderRepository Writes(StoreContext? context = null)
        => new(context ?? _store.Context, new OrderControls(Reads(context)));

    private CreateOrderCommandHandler Create(string buyer, StoreContext? context = null)
        => new(Writes(context), new FakeBuyerIdentity().AsAuthenticated(buyer), new NoopAppLogger());

    private CreatePendingOrderCommandHandler CreatePending(string buyer)
        => new(Writes(), new FakeBuyerIdentity().AsAuthenticated(buyer), new NoopAppLogger());

    private OrderListQueryHandler List(string buyer)
        => new(Reads(), _store.Mapper, new NoopAppLogger(), new FakeBuyerIdentity().AsAuthenticated(buyer));

    private OrderDetailQueryHandler Detail(string buyer)
        => new(Reads(), _store.Mapper, new NoopAppLogger(), new FakeBuyerIdentity().AsAuthenticated(buyer));

    private static ShippingAddress Address(string name = "Buyer") => new()
    {
        FullName = name, Address1 = "Street 1", Address2 = "Unit 2", City = "City",
        State = "State", Zip = "12345", Country = "Country"
    };

    private async Task<(string Buyer, int ProductId)> SeedBasket(long price = 300, int quantity = 2)
    {
        var buyer = Guid.NewGuid().ToString("N");
        var product = _store.SeedProduct(name: "Ordered shoe", price: price);
        var basket = new BasketEntity { BuyerId = buyer, PaymentIntentId = "pi-checkout" };
        basket.AddItem(product, quantity);
        _store.Context.Attach(product);
        _store.Context.Baskets.Add(basket);
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();
        return (buyer, product.ProductId);
    }

    [Fact]
    public async Task Pending_checkout_snapshots_the_order_and_reuses_the_key_only_for_the_same_buyer()
    {
        var first = await SeedBasket();
        var second = await SeedBasket(1_000);
        var request = new CreatePendingOrderCommand { CheckoutRequestId = Guid.NewGuid(), ShippingAddress = Address() };
        var context = _store.Context;
        var basket = await context.Baskets.SingleAsync(x => x.BuyerId == first.Buyer);
        basket.PaymentIntentId = null;
        await context.SaveChangesAsync();

        var created = await CreatePending(first.Buyer).Handle(request, CancellationToken.None);
        created.Succeeded.Should().BeTrue(created.Error);
        var product = await context.Products.SingleAsync(x => x.ProductId == first.ProductId);
        product.QuantityInStock.Should().Be(10);
        product.Price = 999;
        await context.SaveChangesAsync();
        request.ShippingAddress = Address("Changed");
        var replay = await CreatePending(first.Buyer).Handle(request, CancellationToken.None);
        var other = await CreatePending(second.Buyer).Handle(request, CancellationToken.None);
        replay.Succeeded.Should().BeTrue(replay.Error);
        replay.Key.Should().Be(created.Key);
        other.Succeeded.Should().BeTrue(other.Error);
        other.Key.Should().NotBe(created.Key);

        var saved = await Detail(first.Buyer).Handle(new OrderDetailQuery { Id = created.Key }, CancellationToken.None);
        saved.Succeeded.Should().BeTrue(saved.Error);
        saved.Data!.Total.Should().Be(500_600);
        saved.Data.OrderItems.Should().ContainSingle().Which.Price.Should().Be(300);
        saved.Data.ShippingAddress.FullName.Should().Be("Buyer");
        saved.Data.OrderStatus.Should().Be("Pending");
        (await context.Orders.AsNoTracking().SingleAsync(x => x.OrderId == created.Key)).PaymentIntentId.Should().BeNull();
        (await context.Orders.CountAsync(x => x.BuyerId == first.Buyer)).Should().Be(1);
        (await context.Baskets.AnyAsync(x => x.BuyerId == first.Buyer)).Should().BeTrue();
    }

    [Fact]
    public async Task Pending_checkout_rejects_an_empty_request_key()
    {
        var result = await CreatePending("missing-key").Handle(
            new CreatePendingOrderCommand { ShippingAddress = Address() }, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("checkout request ID");
    }

    [Theory]
    [InlineData(PaymentAttemptStatus.Pending, true, 500_600)]
    [InlineData(PaymentAttemptStatus.Succeeded, false, 500_600)]
    [InlineData(PaymentAttemptStatus.Pending, false, 0)]
    public async Task Payment_attempts_preserve_retries_and_reject_conflicting_records(
        PaymentAttemptStatus status, bool reuseAuthority, long amountInRials)
    {
        var seed = await SeedBasket();
        var order = await Create(seed.Buyer).Handle(
            new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);
        order.Succeeded.Should().BeTrue(order.Error);
        var context = _store.Context;
        var firstAuthority = Guid.NewGuid().ToString("D");
        context.PaymentAttempts.AddRange(
            new PaymentAttemptEntity { OrderId = order.Key, AmountInRials = 500_600, Status = PaymentAttemptStatus.Failed },
            new PaymentAttemptEntity { OrderId = order.Key, AmountInRials = 500_600, Status = PaymentAttemptStatus.Pending },
            new PaymentAttemptEntity
            {
                OrderId = order.Key, AmountInRials = 500_600, Status = PaymentAttemptStatus.Succeeded,
                Authority = firstAuthority, RefId = 123456
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.PaymentAttempts.Add(new PaymentAttemptEntity
        {
            OrderId = order.Key, Status = status, AmountInRials = amountInRials,
            Authority = reuseAuthority ? firstAuthority : Guid.NewGuid().ToString("D")
        });
        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        finally
        {
            context.ChangeTracker.Clear();
        }

        var attempts = await context.PaymentAttempts.AsNoTracking().Where(x => x.OrderId == order.Key).ToListAsync();
        attempts.Should().HaveCount(3);
        attempts.Should().ContainSingle(x => x.Status == PaymentAttemptStatus.Succeeded)
            .Which.RefId.Should().Be(123456);
    }

    [Theory]
    [InlineData(5_000_000, 500_000)]
    [InlineData(5_000_001, 0)]
    public async Task Checkout_persists_snapshot_stock_and_basket_removal(long price, long deliveryFee)
    {
        var seed = await SeedBasket(price);
        var result = await Create(seed.Buyer).Handle(
            new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);

        result.Result.Should().Be(ReturnResult.Success, result.Error);
        result.Key.Should().BeGreaterThan(0);
        var saved = await _store.Context.Orders.AsNoTracking().Include(o => o.OrderItems)
            .SingleAsync(o => o.OrderId == result.Key);
        saved.BuyerId.Should().Be(seed.Buyer);
        saved.PaymentIntentId.Should().Be("pi-checkout");
        saved.SubTotal.Should().Be(price * 2);
        saved.DeliveryFee.Should().Be(deliveryFee);
        saved.OrderDate.Kind.Should().Be(DateTimeKind.Unspecified, "SQLite does not preserve DateTime.Kind");
        saved.OrderItems.Should().ContainSingle();
        saved.OrderItems[0].Price.Should().Be(price);
        saved.OrderItems[0].ItemOrdered.Name.Should().Be("Ordered shoe");
        (await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == seed.ProductId))
            .QuantityInStock.Should().Be(8);
        (await _store.Context.Baskets.AnyAsync(b => b.BuyerId == seed.Buyer)).Should().BeFalse();

        var repeated = await Create(seed.Buyer).Handle(
            new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);
        repeated.Succeeded.Should().BeFalse();
        (await _store.Context.Orders.CountAsync(o => o.BuyerId == seed.Buyer)).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkout_saves_a_new_or_existing_user_address(bool existingAddress)
    {
        var seed = await SeedBasket();
        var user = new User { UserName = seed.Buyer, NormalizedUserName = seed.Buyer.ToUpperInvariant() };
        if (existingAddress)
            user.Address = new UserAddress
            {
                FullName = "Old", Address1 = "Old", Address2 = "", City = "Old",
                State = "Old", Zip = "Old", Country = "Old"
            };
        _store.Context.Users.Add(user);
        await _store.Context.SaveChangesAsync();
        var oldAddressId = user.Address?.Id;
        _store.Context.ChangeTracker.Clear();

        var result = await Create(seed.Buyer).Handle(
            new CreateOrderCommand { SaveAddress = true, ShippingAddress = Address("Updated") }, CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Error);
        var saved = await _store.Context.Users.AsNoTracking().Include(u => u.Address).SingleAsync(u => u.UserName == seed.Buyer);
        saved.Address.FullName.Should().Be("Updated");
        if (existingAddress)
            saved.Address.Id.Should().Be(oldAddressId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_empty_basket_cannot_create_an_order(bool emptyBasket)
    {
        var buyer = Guid.NewGuid().ToString("N");
        if (emptyBasket)
        {
            _store.Context.Baskets.Add(new BasketEntity { BuyerId = buyer });
            await _store.Context.SaveChangesAsync();
            _store.Context.ChangeTracker.Clear();
        }

        var result = await Create(buyer).Handle(new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("empty");
        (await _store.Context.Orders.AnyAsync(o => o.BuyerId == buyer)).Should().BeFalse();
    }

    [Fact]
    public async Task Insufficient_stock_on_a_later_line_leaves_all_stock_and_the_basket_unchanged()
    {
        var seed = await SeedBasket();
        var second = _store.SeedProduct(name: "Unavailable");
        var basket = await _store.Context.Baskets.Include(b => b.Items).SingleAsync(b => b.BuyerId == seed.Buyer);
        _store.Context.Attach(second);
        basket.AddItem(second, 11);
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();

        var result = await Create(seed.Buyer).Handle(new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("stock");
        (await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == seed.ProductId)).QuantityInStock.Should().Be(10);
        (await _store.Context.Baskets.AsNoTracking().Include(b => b.Items).SingleAsync(b => b.BuyerId == seed.Buyer)).Items.Should().HaveCount(2);
        (await _store.Context.Orders.AnyAsync(o => o.BuyerId == seed.Buyer)).Should().BeFalse();
    }

    [Fact]
    public async Task Missing_shipping_address_does_not_consume_stock_or_basket()
    {
        var seed = await SeedBasket();
        var result = await Create(seed.Buyer).Handle(new CreateOrderCommand(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Shipping address");
        (await _store.Context.Baskets.AnyAsync(b => b.BuyerId == seed.Buyer)).Should().BeTrue();
        (await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == seed.ProductId)).QuantityInStock.Should().Be(10);
    }

    [Fact]
    public async Task Failure_after_database_writes_rolls_back_order_stock_basket_and_address()
    {
        var seed = await SeedBasket();
        _store.Context.Users.Add(new User { UserName = seed.Buyer, NormalizedUserName = seed.Buyer.ToUpperInvariant() });
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();
        var options = new DbContextOptionsBuilder<StoreContext>()
            .UseSqlite(_store.Context.Database.GetDbConnection())
            .AddInterceptors(new FailAfterSave()).Options;
        await using var failingContext = new StoreContext(options);

        var result = await Create(seed.Buyer, failingContext).Handle(
            new CreateOrderCommand { SaveAddress = true, ShippingAddress = Address() }, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("Checkout failure after save");
        (await _store.Context.Orders.AnyAsync(o => o.BuyerId == seed.Buyer)).Should().BeFalse();
        (await _store.Context.Baskets.Include(b => b.Items).AsNoTracking().SingleAsync(b => b.BuyerId == seed.Buyer)).Items.Should().ContainSingle();
        (await _store.Context.Products.AsNoTracking().SingleAsync(p => p.ProductId == seed.ProductId)).QuantityInStock.Should().Be(10);
        (await _store.Context.Users.AsNoTracking().Include(u => u.Address).SingleAsync(u => u.UserName == seed.Buyer)).Address.Should().BeNull();
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Checkout failure after save");
    }

    [Fact]
    public async Task Reads_enforce_ownership_and_return_nested_snapshots_with_paging_and_filters()
    {
        var first = await SeedBasket();
        var second = await SeedBasket();
        var created = await Create(first.Buyer).Handle(new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);
        await Create(second.Buyer).Handle(new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);

        // A product edit after checkout must not alter the order snapshot.
        var product = await _store.Context.Products.SingleAsync(p => p.ProductId == first.ProductId);
        product.Name = "Renamed product";
        product.Price = 999;
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();

        var list = await List(first.Buyer).Handle(new OrderListQuery
        {
            PagingInfo = new PagingInfo { PageNumber = 1, PageRecordCount = 1 },
            Filters = new() { new FilterInfo { ColName = nameof(OrderEntity.SubTotal), FilterType = eFilterType.ftGreaterThan, Value = 0 } }
        }, CancellationToken.None);
        list.Succeeded.Should().BeTrue(list.Error);
        list.TotalCount.Should().Be(1);
        list.Data.Should().ContainSingle().Which.BuyerId.Should().Be(first.Buyer);
        list.Data[0].OrderItems.Should().ContainSingle().Which.Name.Should().Be("Ordered shoe");
        list.Data[0].OrderItems[0].Price.Should().Be(300);
        list.Data[0].Total.Should().Be(500600);

        var own = await Detail(first.Buyer).Handle(new OrderDetailQuery { Id = created.Key }, CancellationToken.None);
        own.Succeeded.Should().BeTrue(own.Error);
        own.Data!.ShippingAddress.FullName.Should().Be("Buyer");
        own.Data.Id.Should().Be(created.Key);

        var other = await Detail(second.Buyer).Handle(new OrderDetailQuery { Id = created.Key }, CancellationToken.None);
        other.Succeeded.Should().BeFalse();
        other.Data.Should().BeNull();
    }

    [Fact]
    public async Task Anonymous_identity_cannot_read_or_create_orders_even_with_a_buyer_cookie()
    {
        var seed = await SeedBasket();
        var buyer = new FakeBuyerIdentity { BuyerId = seed.Buyer };
        var logger = new NoopAppLogger();
        var create = await new CreateOrderCommandHandler(Writes(), buyer, logger).Handle(
            new CreateOrderCommand { ShippingAddress = Address() }, CancellationToken.None);
        var list = await new OrderListQueryHandler(Reads(), _store.Mapper, logger, buyer)
            .Handle(new OrderListQuery(), CancellationToken.None);
        var detail = await new OrderDetailQueryHandler(Reads(), _store.Mapper, logger, buyer)
            .Handle(new OrderDetailQuery { Id = 1 }, CancellationToken.None);

        create.Succeeded.Should().BeFalse();
        list.Succeeded.Should().BeFalse();
        detail.Succeeded.Should().BeFalse();
        (await _store.Context.Baskets.AnyAsync(b => b.BuyerId == seed.Buyer)).Should().BeTrue();
    }
}
