using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.Controls;
using ReStore.Application.Handlers.CommandHandlers;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Domain.Interfaces.Common;
using ReStore.Infrastructure.Persistence;
using ReStore.Infrastructure.Repositories;
using ReStore.Infrastructure.Services;
using Stripe;
using Xunit;

namespace ReStore.Test;

public sealed class PaymentSliceTests : IDisposable
{
    private const string SigningSecret = "whsec_local_test_only";
    private readonly SqliteStore _store = new();

    private PaymentControls Controls() => new(new GenericQueryRepositoryBase<BasketEntity>(_store.Context, _store.Mapper));

    private CreateOrUpdatePaymentIntentCommandHandler Handler(IPaymentGateway gateway, FakeBuyerIdentity? buyer = null,
        IBasketRepository? baskets = null, IPaymentRepository? payments = null)
        => new(baskets ?? new BasketRepository(_store.Context), payments ?? new PaymentRepository(_store.Context),
            gateway, Controls(), buyer ?? new FakeBuyerIdentity().AsAuthenticated("Alice"), new NoopAppLogger());

    private static Mock<IPaymentGateway> Gateway()
    {
        var gateway = new Mock<IPaymentGateway>();
        gateway.Setup(g => g.CreateOrUpdateAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponseOne<PaymentIntentInfo>(new PaymentIntentInfo("pi-test", "secret-test")));
        return gateway;
    }

    private async Task<int> SeedBasket(long price = 300, bool empty = false)
    {
        var basket = new BasketEntity { BuyerId = "Alice" };
        if (!empty)
        {
            var product = _store.SeedProduct(price: price);
            _store.Context.Attach(product);
            basket.AddItem(product, 2);
        }
        _store.Context.Baskets.Add(basket);
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();
        return basket.BasketId;
    }

    [Theory]
    [InlineData(5_000_000, 10_500_000)]
    [InlineData(5_000_001, 10_000_002)]
    public async Task Payment_amount_uses_database_prices_and_delivery_and_persists_only_payment_fields(long price, long amount)
    {
        var basketId = await SeedBasket(price);
        var gateway = Gateway();
        var result = await Handler(gateway.Object).Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Data!.Id.Should().Be(basketId);
        result.Data.PaymentIntentId.Should().Be("pi-test");
        result.Data.ClientSecret.Should().Be("secret-test");
        gateway.Verify(g => g.CreateOrUpdateAsync(amount, null,
            It.Is<string>(key => key.Contains($"basket-{basketId}") && key.Contains($"amount-{amount}")), CancellationToken.None), Times.Once);
        var saved = await _store.Context.Baskets.AsNoTracking().Include(b => b.Items).ThenInclude(i => i.Product).SingleAsync();
        saved.PaymentIntentId.Should().Be("pi-test");
        saved.Items.Should().ContainSingle();
        saved.Items[0].Product.Price.Should().Be(price);
        saved.Items[0].Product.QuantityInStock.Should().Be(10);
    }

    [Fact]
    public async Task Repeated_payment_requests_update_the_existing_intent_and_can_succeed_without_other_basket_changes()
    {
        await SeedBasket();
        var gateway = Gateway();
        var handler = Handler(gateway.Object);
        var first = await handler.Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        var second = await handler.Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        var third = await handler.Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        first.Succeeded.Should().BeTrue(first.Error);
        second.Succeeded.Should().BeTrue(second.Error);
        third.Succeeded.Should().BeTrue(third.Error);
        gateway.Verify(g => g.CreateOrUpdateAsync(500600, "pi-test", It.IsAny<string>(), CancellationToken.None), Times.Exactly(2));
        var keys = gateway.Invocations.Select(call => (string)call.Arguments[2]).ToArray();
        keys[1].Should().Be(keys[2], "retrying the same update reuses its idempotency key");
        (await _store.Context.Products.CountAsync()).Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_empty_baskets_never_call_the_gateway(bool emptyBasket)
    {
        if (emptyBasket) await SeedBasket(empty: true);
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var result = await Handler(gateway.Object).Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("empty");
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Anonymous_buyer_with_an_account_cookie_cannot_create_a_payment_intent()
    {
        await SeedBasket();
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var result = await Handler(gateway.Object, new FakeBuyerIdentity { BuyerId = "Alice" })
            .Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Gateway_failure_does_not_persist_payment_columns()
    {
        await SeedBasket();
        var gateway = Gateway();
        gateway.Setup(g => g.CreateOrUpdateAsync(It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponseOne<PaymentIntentInfo>("Stripe unavailable"));
        var result = await Handler(gateway.Object).Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Stripe unavailable");
        (await _store.Context.Baskets.AsNoTracking().SingleAsync()).PaymentIntentId.Should().BeNull();
    }

    [Fact]
    public async Task Database_read_failure_is_not_reported_as_an_empty_basket()
    {
        var baskets = new Mock<IBasketRepository>();
        baskets.Setup(b => b.GetWithItemsAsync("Alice", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponseOne<BasketEntity>(new InvalidOperationException("Database unavailable")));
        var gateway = new Mock<IPaymentGateway>(MockBehavior.Strict);
        var result = await Handler(gateway.Object, baskets: baskets.Object).Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Database unavailable");
        gateway.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Payment_columns_cannot_be_saved_to_a_different_buyer_or_over_a_competing_intent()
    {
        var basketId = await SeedBasket();
        var repository = new PaymentRepository(_store.Context);
        var intent = new PaymentIntentInfo("pi-test", "secret-test");
        (await repository.SaveIntentAsync(basketId, "Other", null, intent, CancellationToken.None)).Result.Should().BeFalse();
        (await repository.SaveIntentAsync(basketId, "Alice", null, intent, CancellationToken.None)).Result.Should().BeTrue();
        (await repository.SaveIntentAsync(basketId, "Alice", null,
            new PaymentIntentInfo("pi-competing", "other-secret"), CancellationToken.None)).Result.Should().BeFalse();
        (await _store.Context.Baskets.AsNoTracking().SingleAsync()).PaymentIntentId.Should().Be("pi-test");
    }

    [Fact]
    public async Task Failure_persisting_an_intent_returns_a_failed_envelope()
    {
        await SeedBasket();
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(r => r.SaveIntentAsync(It.IsAny<int>(), "Alice", null, It.IsAny<PaymentIntentInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DomainResponse(new InvalidOperationException("Intent save failed")));
        var result = await Handler(Gateway().Object, payments: repository.Object)
            .Handle(new CreateOrUpdatePaymentIntentCommand(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("Intent save failed");
        result.Data.Should().BeNull();
    }

    private static StripePaymentGateway StripeGateway()
        => new(Mock.Of<IConfiguration>(config => config["StripeSettings:WhSecret"] == SigningSecret));

    private static ProcessPaymentWebhookCommand Event(string objectType = "charge", string status = "succeeded", string intentId = "pi-test")
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt-local", @object = "event", api_version = StripeConfiguration.ApiVersion,
            created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), livemode = false,
            request = new { id = "req-local", idempotency_key = (string?)null },
            type = objectType == "charge" ? "charge.updated" : "payment_intent.created",
            data = new { @object = new { id = "object-local", @object = objectType, status, payment_intent = intentId } }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var digest = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningSecret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return new ProcessPaymentWebhookCommand { Payload = payload, Signature = $"t={timestamp},v1={Convert.ToHexString(digest).ToLowerInvariant()}" };
    }

    private async Task<int> SeedOrder(string intent = "pi-test")
    {
        var order = new OrderEntity
        {
            BuyerId = "Alice", PaymentIntentId = intent, SubTotal = 600, DeliveryFee = 500,
            ShippingAddress = new()
            {
                FullName = "Alice", Address1 = "Street", Address2 = "", City = "City", State = "State", Zip = "123", Country = "Country"
            },
            OrderItems = new()
            {
                new OrderItemEntity { ItemOrdered = new() { ProductId = 1, Name = "Shoe", PictureUrl = "" }, Quantity = 2, Price = 300 }
            }
        };
        _store.Context.Orders.Add(order);
        await _store.Context.SaveChangesAsync();
        _store.Context.ChangeTracker.Clear();
        return order.OrderId;
    }

    [Theory]
    [InlineData("succeeded", OrderStatus.PaymentReceived)]
    [InlineData("failed", OrderStatus.PaymentFailed)]
    public async Task Signed_terminal_events_update_only_the_matching_order(string status, OrderStatus expected)
    {
        var orderId = await SeedOrder();
        var otherId = await SeedOrder("pi-other");
        var handler = new ProcessPaymentWebhookCommandHandler(StripeGateway(), new PaymentRepository(_store.Context), new NoopAppLogger());
        var result = await handler.Handle(Event(status: status), CancellationToken.None);
        result.Succeeded.Should().BeTrue(result.Error);
        (await _store.Context.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId)).OrderStatus.Should().Be(expected);
        (await _store.Context.Orders.AsNoTracking().SingleAsync(o => o.OrderId == otherId)).OrderStatus.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public async Task Duplicate_success_and_late_failure_do_not_downgrade_a_paid_order()
    {
        var orderId = await SeedOrder();
        var handler = new ProcessPaymentWebhookCommandHandler(StripeGateway(), new PaymentRepository(_store.Context), new NoopAppLogger());
        foreach (var status in new[] { "failed", "succeeded", "succeeded", "failed" })
            (await handler.Handle(Event(status: status), CancellationToken.None)).Succeeded.Should().BeTrue();
        (await _store.Context.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId)).OrderStatus.Should().Be(OrderStatus.PaymentReceived);
    }

    [Theory]
    [InlineData("charge", "pending")]
    [InlineData("payment_intent", "succeeded")]
    public async Task Signed_pending_or_unrelated_events_are_acknowledged_without_database_access(string objectType, string status)
    {
        var repository = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var result = await new ProcessPaymentWebhookCommandHandler(StripeGateway(), repository.Object, new NoopAppLogger())
            .Handle(Event(objectType, status), CancellationToken.None);
        result.Succeeded.Should().BeTrue(result.Error);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_signed_event_without_a_matching_order_is_acknowledged()
    {
        var result = await new ProcessPaymentWebhookCommandHandler(StripeGateway(), new PaymentRepository(_store.Context), new NoopAppLogger())
            .Handle(Event(), CancellationToken.None);
        result.Succeeded.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Invalid_signatures_are_rejected_before_any_database_write()
    {
        var request = Event();
        request.Payload += " ";
        var repository = new Mock<IPaymentRepository>(MockBehavior.Strict);
        var result = await new ProcessPaymentWebhookCommandHandler(StripeGateway(), repository.Object, new NoopAppLogger())
            .Handle(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be("InvalidWebhook");
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_webhook_database_failure_remains_a_failure_for_the_controller_to_retry()
    {
        var options = new DbContextOptionsBuilder<StoreContext>().UseSqlite("DataSource=:memory:").Options;
        await using var context = new StoreContext(options);
        var result = await new ProcessPaymentWebhookCommandHandler(StripeGateway(), new PaymentRepository(context), new NoopAppLogger())
            .Handle(Event(), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().NotBe("InvalidWebhook");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pi-existing")]
    public async Task Rial_payments_never_create_or_update_a_USD_Stripe_intent(string? existingIntent)
    {
        var client = new Mock<IStripeClient>(MockBehavior.Strict);
        var gateway = new StripePaymentGateway(Mock.Of<IConfiguration>(), client.Object);
        var result = await gateway.CreateOrUpdateAsync(1_000_000, existingIntent, "key", CancellationToken.None);
        result.Result.Should().BeFalse();
        result.Error.Should().Contain("ZarinPal");
        client.VerifyNoOtherCalls();
    }

    public void Dispose() => _store.Dispose();
}
