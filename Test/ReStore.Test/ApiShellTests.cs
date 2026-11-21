using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ReStore.Api.Controllers;
using ReStore.Api.Extensions;
using ReStore.Application.Commands;
using ReStore.Application.Common;
using ReStore.Application.Queries;
using ReStore.Infrastructure.Persistence;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ReStore.Test;

public sealed class ApiShellHost : IAsyncLifetime
{
    internal const string TokenKey = "restore-api-shell-tests-0123456789-abcdefghijklmnopqrstuvwxyz-0123456789";
    private WebApplication _app = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ProductsController).Assembly.GetName().Name,
            EnvironmentName = "Production",
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../SourceApi/ReStore.Api"))
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWTSettings:TokenKey"] = TokenKey,
            ["Logging:EventLog:LogLevel:Default"] = "None"
        });
        builder.Services.AddDbContext<StoreContext>(options => options.UseSqlite("DataSource=:memory:"));
        builder.Services.AddApiServices(builder.Configuration);

        // These tests exercise the HTTP shell with a controlled application failure;
        // the actual CQRS handlers and repositories are covered by the slice tests.
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<ProductListQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Private database diagnostic"));
        builder.Services.AddSingleton(mediator.Object);

        _app = builder.Build();
        _app.UseApi();
        await _app.StartAsync();
        Client = _app.GetTestClient();
    }

    public static string MemberToken()
    {
        var token = new JwtSecurityToken(claims: new[] { new Claim(ClaimTypes.Name, "Alice"), new Claim(ClaimTypes.Role, "Member") },
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TokenKey)), SecurityAlgorithms.HmacSha512));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}

public class ApiShellTests(ApiShellHost host) : IClassFixture<ApiShellHost>
{
    [Theory]
    [InlineData("/aiAssistant/chat")]
    [InlineData("/api/aiAssistant/chat")]
    public async Task Assistant_echoes_the_submitted_message(string path)
    {
        const string message = "  سلام ReStore!\nپیام دوم  ";
        using var response = await host.Client.PostAsJsonAsync(path, new { message });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = await response.Content.ReadFromJsonAsync<AiAssistantMessage>();
        reply!.Message.Should().Be(message);
    }

    [Theory]
    [InlineData("GET", "/api/products?pageNumber=invalid", null)]
    [InlineData("POST", "/api/basket?productId=1&quantity=invalid", "{}")]
    [InlineData("POST", "/api/account/login", "{\"username\":")]
    public async Task Feature_binding_failures_use_the_envelope(string method, string path, string? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = await host.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("ValidationFailed");
        json.RootElement.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Unexpected_feature_exceptions_use_a_failed_envelope_without_production_details()
    {
        var response = await host.Client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("errorCode").GetString().Should().Be("UnhandledError");
        json.RootElement.GetProperty("error").GetString().Should().Be("Internal server error");
        body.Should().NotContain("Private database diagnostic");
    }

    [Theory]
    [InlineData("not-found", 404)]
    [InlineData("bad-request", 400)]
    [InlineData("unauthorized", 401)]
    [InlineData("validation-error", 400)]
    [InlineData("server-error", 500)]
    public async Task Educational_error_endpoints_keep_their_intended_statuses(string action, int status)
    {
        var response = await host.Client.GetAsync($"/api/buggy/{action}");
        ((int)response.StatusCode).Should().Be(status);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("\"succeeded\"");
        if (action == "validation-error")
        {
            using var json = JsonDocument.Parse(body);
            json.RootElement.GetProperty("errors").GetProperty("Problem1")[0].GetString().Should().Be("This is the first error");
        }
    }

    [Fact]
    public async Task Authentication_and_role_denials_keep_401_and_403()
    {
        (await host.Client.GetAsync("/api/orders")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiShellHost.MemberToken());
        request.Content = new MultipartFormDataContent();
        (await host.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/health/app")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/")]
    public async Task Health_swagger_and_the_spa_remain_available(string path)
        => (await host.Client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task The_existing_react_origin_keeps_its_credentialed_cors_policy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/account/login");
        request.Headers.Add("Origin", "http://localhost:3000");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await host.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle().Which.Should().Be("http://localhost:3000");
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should().ContainSingle().Which.Should().Be("true");
    }
}
