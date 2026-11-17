using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReStore.Infrastructure.IoC;
using ReStore.Infrastructure.Payments;
using Xunit;

namespace ReStore.Test;

public sealed class ZarinpalSettingsTests
{
    private const string OtherMerchant = "22222222-2222-4222-8222-222222222222";

    private static IConfiguration Configuration(string mode = "Sandbox", string? merchant = ZarinpalSettings.SandboxMerchantId,
        string? callback = "http://localhost:5000/api/payments/zarinpal/callback")
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ZarinpalSettings:Mode"] = mode,
            ["ZarinpalSettings:MerchantId"] = merchant,
            ["ZarinpalSettings:CallbackUrl"] = callback
        }).Build();

    [Fact]
    public void Checked_in_settings_select_the_sandbox_and_loopback_callback()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../SourceApi/ReStore.Api/appsettings.json"));
        var settings = ZarinpalSettings.FromConfiguration(new ConfigurationBuilder().AddJsonFile(path).Build());
        settings.Mode.Should().Be(ZarinpalMode.Sandbox);
        settings.BaseAddress.AbsoluteUri.Should().Be("https://sandbox.zarinpal.com/");
        settings.MerchantId.Should().Be(ZarinpalSettings.SandboxMerchantId);
        settings.CallbackUrl.IsLoopback.Should().BeTrue();
    }

    [Fact]
    public void Production_configuration_selects_the_live_host_and_normalizes_the_merchant_UUID()
    {
        var settings = ZarinpalSettings.FromConfiguration(Configuration("Production", OtherMerchant,
            "https://shop.example.com/api/payments/zarinpal/callback"));
        settings.Mode.Should().Be(ZarinpalMode.Production);
        settings.BaseAddress.AbsoluteUri.Should().Be("https://payment.zarinpal.com/");
        settings.MerchantId.Should().Be(OtherMerchant);
        settings.CallbackUrl.Scheme.Should().Be("https");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("Sandbx")]
    public void Invalid_modes_do_not_fall_back_to_any_environment(string mode)
    {
        var act = () => ZarinpalSettings.FromConfiguration(Configuration(mode));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Mode*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-merchant")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Missing_or_malformed_merchants_are_rejected(string? merchant)
    {
        var act = () => ZarinpalSettings.FromConfiguration(Configuration(merchant: merchant));
        act.Should().Throw<InvalidOperationException>().WithMessage("*MerchantId*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/api/payments/callback")]
    [InlineData("file:///tmp/callback")]
    [InlineData("https://shop.example.com/callback#result")]
    [InlineData("https://user:private-value@shop.example.com/callback")]
    [InlineData("http://shop.example.com/callback")]
    public void Unsafe_or_invalid_callbacks_are_rejected_without_echoing_the_value(string? callback)
    {
        var act = () => ZarinpalSettings.FromConfiguration(Configuration(callback: callback));
        act.Should().Throw<InvalidOperationException>().WithMessage("*CallbackUrl*")
            .Which.Message.Should().NotContain("private-value");
    }

    [Theory]
    [InlineData("http://shop.example.com/callback")]
    [InlineData("http://localhost:5000/callback")]
    [InlineData("https://localhost/callback")]
    [InlineData("https://127.0.0.1/callback")]
    public void Production_callbacks_require_HTTPS_and_a_non_loopback_host(string callback)
    {
        var act = () => ZarinpalSettings.FromConfiguration(Configuration("Production", OtherMerchant, callback));
        act.Should().Throw<InvalidOperationException>().WithMessage("*CallbackUrl*");
    }

    [Fact]
    public void Production_rejects_the_checked_in_test_merchant()
    {
        var act = () => ZarinpalSettings.FromConfiguration(Configuration("Production",
            callback: "https://shop.example.com/callback"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*real merchant ID*");
    }

    [Fact]
    public void Later_configuration_providers_override_the_sandbox_defaults()
    {
        var configuration = new ConfigurationBuilder().AddConfiguration(Configuration()).AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ZarinpalSettings:Mode"] = "Production",
                ["ZarinpalSettings:MerchantId"] = OtherMerchant,
                ["ZarinpalSettings:CallbackUrl"] = "https://shop.example.com/callback"
            }).Build();
        var services = new ServiceCollection().AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ZarinpalSettings>().Mode.Should().Be(ZarinpalMode.Production);
    }

    [Fact]
    public void Invalid_configuration_fails_during_registration_without_database_or_gateway_access()
    {
        var act = () => new ServiceCollection().AddInfrastructure(Configuration(mode: "typo"));
        act.Should().Throw<InvalidOperationException>().WithMessage("*ZarinpalSettings*Mode*");
    }
}
