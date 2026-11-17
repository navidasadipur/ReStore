using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ReStore.Domain.Entities.OrderAggregare;
using ReStore.Infrastructure.Payments;
using Xunit;

namespace ReStore.Test;

public sealed class ZarinpalPaymentServiceTests
{
    private static readonly string Authority = "S" + new string('0', 35);

    [Fact]
    public async Task Uses_the_saved_total_and_persists_the_authority_before_returning_the_link()
    {
        using var store = new SqliteStore();
        var order = await SaveOrderAsync(store);
        var response = JsonSerializer.Serialize(new { data = new { code = 100, authority = Authority } });
        using var transport = new GatewayTransport(response);
        using var http = new HttpClient(transport);
        var service = new ZarinpalPaymentService(http, Settings(), store.Context);

        var first = await service.StartAsync(order.BuyerId, order.OrderId, CancellationToken.None);
        var second = await service.StartAsync(order.BuyerId, order.OrderId, CancellationToken.None);

        first.Result.Should().BeTrue(first.Error);
        first.Data!.PaymentUrl.AbsoluteUri.Should().Be($"https://sandbox.zarinpal.com/pg/StartPay/{Authority}");
        second.Data!.PaymentUrl.Should().Be(first.Data.PaymentUrl);
        transport.Calls.Should().Be(1);
        transport.Target!.AbsoluteUri.Should().Be("https://sandbox.zarinpal.com/pg/v4/payment/request.json");
        using var sent = JsonDocument.Parse(transport.Body!);
        sent.RootElement.GetProperty("amount").GetInt64().Should().Be(1_500_000);
        sent.RootElement.GetProperty("currency").GetString().Should().Be("IRR");
        sent.RootElement.GetProperty("merchant_id").GetString().Should().Be(ZarinpalSettings.SandboxMerchantId);
        sent.RootElement.GetProperty("callback_url").GetString().Should().Be("http://localhost:5000/api/payments/zarinpal/callback");
        var saved = await store.Context.PaymentAttempts.AsNoTracking().SingleAsync();
        saved.OrderId.Should().Be(order.OrderId);
        saved.Authority.Should().Be(Authority);
        saved.AmountInRials.Should().Be(1_500_000);
        saved.Status.Should().Be(PaymentAttemptStatus.Pending);
        saved.IsSandbox.Should().BeTrue();
    }

    [Fact]
    public async Task Another_buyer_and_already_paid_orders_cannot_start_payment()
    {
        using var store = new SqliteStore();
        var order = await SaveOrderAsync(store);
        using var transport = new GatewayTransport("{}");
        using var http = new HttpClient(transport);
        var service = new ZarinpalPaymentService(http, Settings(), store.Context);
        (await service.StartAsync("another buyer", order.OrderId, CancellationToken.None)).Result.Should().BeFalse();
        order.OrderStatus = OrderStatus.PaymentReceived;
        await store.Context.SaveChangesAsync();
        (await service.StartAsync(order.BuyerId, order.OrderId, CancellationToken.None)).Result.Should().BeFalse();
        transport.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("""{"data":{"code":-9}}""")]
    [InlineData("""{"data":{"code":100,"authority":"../../wrong-host"}}""")]
    public async Task Rejected_responses_leave_no_payment_attempt(string response)
    {
        using var store = new SqliteStore();
        var order = await SaveOrderAsync(store);
        using var transport = new GatewayTransport(response);
        using var http = new HttpClient(transport);
        var service = new ZarinpalPaymentService(http, Settings(), store.Context);
        (await service.StartAsync(order.BuyerId, order.OrderId, CancellationToken.None)).Result.Should().BeFalse();
        (await store.Context.PaymentAttempts.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(100, 987654L, 10, 8)]
    [InlineData(100, 987654L, 1, 1)]
    [InlineData(101, null, 10, 8)]
    public async Task Verified_callbacks_keep_payment_and_stock_consistent_on_replay(
        int code, long? reference, int initialStock, int expectedStock)
    {
        using var store = new SqliteStore();
        var (orderId, productId) = await SaveAttemptAsync(store, initialStock);
        var response = JsonSerializer.Serialize(new { data = new { code, ref_id = reference } });
        using var transport = new GatewayTransport(response);
        using var http = new HttpClient(transport);
        var service = new ZarinpalPaymentService(http, Settings(), store.Context);

        var first = await service.VerifyAsync(Authority, "OK", CancellationToken.None);
        var replay = await service.VerifyAsync(Authority, "OK", CancellationToken.None);

        first.Result.Should().BeTrue(first.Error);
        first.Data!.Paid.Should().BeTrue();
        replay.Data!.ReferenceId.Should().Be(reference);
        transport.Calls.Should().Be(1);
        transport.Target!.AbsoluteUri.Should().Be("https://sandbox.zarinpal.com/pg/v4/payment/verify.json");
        using var sent = JsonDocument.Parse(transport.Body!);
        sent.RootElement.GetProperty("amount").GetInt64().Should().Be(1_500_000);
        sent.RootElement.GetProperty("authority").GetString().Should().Be(Authority);
        (await store.Context.Orders.AsNoTracking().SingleAsync(x => x.OrderId == orderId))
            .OrderStatus.Should().Be(OrderStatus.PaymentReceived);
        var attempt = await store.Context.PaymentAttempts.AsNoTracking().SingleAsync();
        attempt.Status.Should().Be(PaymentAttemptStatus.Succeeded);
        attempt.RefId.Should().Be(reference);
        (await store.Context.Products.AsNoTracking().SingleAsync(x => x.ProductId == productId))
            .QuantityInStock.Should().Be(expectedStock);
    }

    [Fact]
    public async Task Cancelled_or_unconfirmed_callbacks_do_not_mark_the_order_paid()
    {
        using var store = new SqliteStore();
        var (orderId, _) = await SaveAttemptAsync(store, 10);
        using var transport = new GatewayTransport("""{"data":{"code":-51}}""");
        using var http = new HttpClient(transport);
        var service = new ZarinpalPaymentService(http, Settings(), store.Context);

        var cancelled = await service.VerifyAsync(Authority, "NOK", CancellationToken.None);
        cancelled.Data!.Paid.Should().BeFalse();
        var unknown = await service.VerifyAsync("S" + new string('1', 35), "OK", CancellationToken.None);
        unknown.Result.Should().BeFalse();
        transport.Calls.Should().Be(0);

        (await service.VerifyAsync(Authority, "OK", CancellationToken.None)).Result.Should().BeFalse();
        transport.Calls.Should().Be(1);
        (await store.Context.PaymentAttempts.AsNoTracking().SingleAsync()).Status.Should().Be(PaymentAttemptStatus.Pending);
        (await store.Context.Orders.AsNoTracking().SingleAsync(x => x.OrderId == orderId))
            .OrderStatus.Should().Be(OrderStatus.Pending);
    }

    private static async Task<(int OrderId, int ProductId)> SaveAttemptAsync(SqliteStore store, int stock)
    {
        var product = store.SeedProduct(price: 500_000);
        store.Context.Products.Attach(product);
        product.QuantityInStock = stock;
        var order = await SaveOrderAsync(store);
        order.OrderItems.Add(new OrderItemEntity
        {
            ItemOrdered = new ProductItemOrdered { ProductId = product.ProductId, Name = "Test", PictureUrl = "" },
            Price = 500_000, Quantity = 2
        });
        store.Context.PaymentAttempts.Add(new PaymentAttemptEntity
        {
            OrderId = order.OrderId, IsSandbox = true, Authority = Authority, AmountInRials = 1_500_000
        });
        await store.Context.SaveChangesAsync();
        store.Context.ChangeTracker.Clear();
        return (order.OrderId, product.ProductId);
    }

    private static ZarinpalSettings Settings() => ZarinpalSettings.FromConfiguration(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ZarinpalSettings:Mode"] = "Sandbox",
            ["ZarinpalSettings:MerchantId"] = ZarinpalSettings.SandboxMerchantId,
            ["ZarinpalSettings:CallbackUrl"] = "http://localhost:5000/api/payments/zarinpal/callback"
        }).Build());

    private static async Task<OrderEntity> SaveOrderAsync(SqliteStore store)
    {
        var order = new OrderEntity
        {
            BuyerId = "buyer", CheckoutRequestId = Guid.NewGuid(), SubTotal = 1_000_000, DeliveryFee = 500_000,
            OrderItems = [],
            ShippingAddress = new ShippingAddress
            {
                FullName = "Buyer", Address1 = "Street", Address2 = "Unit", City = "City",
                State = "State", Country = "Country", Zip = "12345"
            }
        };
        store.Context.Orders.Add(order);
        await store.Context.SaveChangesAsync();
        return order;
    }

    private sealed class GatewayTransport(string response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Target { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Target = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
