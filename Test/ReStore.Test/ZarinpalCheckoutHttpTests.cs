using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReStore.Api.Controllers;
using ReStore.Api.Extensions;
using ReStore.Application.Interfaces;
using ReStore.Application.IoC;
using ReStore.Domain.Entities;
using ReStore.Infrastructure.IoC;
using ReStore.Infrastructure.Payments;
using ReStore.Infrastructure.Persistence;
using Xunit;

namespace ReStore.Test;

public sealed class ZarinpalCheckoutHttpTests
{
    private static readonly string Authority = "S" + new string('0', 35);

    [Fact]
    public async Task Checkout_completes_through_the_real_HTTP_routes_without_Docker()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(PaymentsController).Assembly.GetName().Name,
            EnvironmentName = "Production",
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../SourceApi/ReStore.Api"))
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWTSettings:TokenKey"] = ApiShellHost.TokenKey,
            ["Logging:EventLog:LogLevel:Default"] = "None"
        });
        builder.Services.AddApplication();
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<StoreContext>>();
        builder.Services.AddDbContext<StoreContext>(options => options.UseSqlite(connection));
        builder.Services.AddApiServices(builder.Configuration);
        builder.Services.AddHttpClient<IZarinpalPaymentService, ZarinpalPaymentService>()
            .ConfigurePrimaryHttpMessageHandler(() => new SandboxResponseHandler());
        await using var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StoreContext>();
            await db.Database.EnsureCreatedAsync();
            var product = new ProductEntity
            {
                Name = "Demo shoe", Description = "Demo", Brand = "Demo", Type = "Shoes",
                Price = 1_000_000, QuantityInStock = 10
            };
            var basket = new BasketEntity { BuyerId = "Alice" };
            basket.AddItem(product, 1);
            db.Baskets.Add(basket);
            await db.SaveChangesAsync();
        }
        app.UseApi();
        await app.StartAsync();
        using var http = app.GetTestClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiShellHost.MemberToken());
        var pending = await ReadSuccessAsync(await http.PostAsJsonAsync("/api/orders/pending", new
        {
            checkoutRequestId = Guid.NewGuid(),
            shippingAddress = new { fullName = "Alice", address1 = "Street", address2 = "", city = "City",
                state = "State", country = "Country", zip = "12345" }
        }));
        var orderId = pending.GetProperty("key").GetInt32();
        var payment = await ReadSuccessAsync(await http.PostAsync($"/api/payments/zarinpal/{orderId}", null));
        payment.GetProperty("data").GetProperty("paymentUrl").GetString()
            .Should().Be($"https://sandbox.zarinpal.com/pg/StartPay/{Authority}");

        http.DefaultRequestHeaders.Authorization = null;
        var callback = $"/api/payments/zarinpal/callback?Authority={Authority}&Status=OK";
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var result = (await ReadSuccessAsync(await http.GetAsync(callback))).GetProperty("data");
            result.GetProperty("paid").GetBoolean().Should().BeTrue();
            result.GetProperty("referenceId").GetInt64().Should().Be(123456L);
        }
        await using var check = app.Services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<StoreContext>();
        (await context.Products.SingleAsync()).QuantityInStock.Should().Be(9);
        (await context.PaymentAttempts.CountAsync()).Should().Be(1);
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeTrue(body);
            return json.RootElement.Clone();
        }
    }

    private sealed class SandboxResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var data = request.RequestUri?.AbsolutePath switch
            {
                "/pg/v4/payment/request.json" => JsonContent.Create(new { data = new { code = 100, authority = Authority } }),
                "/pg/v4/payment/verify.json" => JsonContent.Create(new { data = new { code = 100, ref_id = 123456 } }),
                _ => throw new InvalidOperationException("Unexpected gateway request.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = data });
        }
    }
}
