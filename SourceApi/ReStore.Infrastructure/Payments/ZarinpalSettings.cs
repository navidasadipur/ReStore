using Microsoft.Extensions.Configuration;

namespace ReStore.Infrastructure.Payments;

public enum ZarinpalMode { Sandbox, Production }

/// <summary>Validated server-only settings; endpoint hosts are selected by mode.</summary>
public sealed class ZarinpalSettings
{
    public const string SectionName = "ZarinpalSettings";
    public const string SandboxMerchantId = "11111111-1111-4111-8111-111111111111";

    public ZarinpalMode Mode { get; }
    public string MerchantId { get; }
    public Uri CallbackUrl { get; }
    public Uri BaseAddress { get; }

    private ZarinpalSettings(ZarinpalMode mode, string merchantId, Uri callbackUrl)
    {
        Mode = mode;
        MerchantId = merchantId;
        CallbackUrl = callbackUrl;
        BaseAddress = new Uri(mode == ZarinpalMode.Sandbox
            ? "https://sandbox.zarinpal.com/" : "https://payment.zarinpal.com/");
    }

    public static ZarinpalSettings FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var mode = section["Mode"]?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => ZarinpalMode.Sandbox,
            "production" => ZarinpalMode.Production,
            _ => throw Invalid("Mode must be Sandbox or Production.")
        };

        var merchantId = section["MerchantId"]?.Trim();
        if (!Guid.TryParseExact(merchantId, "D", out var merchant) || merchant == Guid.Empty)
            throw Invalid("MerchantId must be a non-empty UUID in xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx format.");
        if (mode == ZarinpalMode.Production && merchant == Guid.Parse(SandboxMerchantId))
            throw Invalid("MerchantId must be replaced with the real merchant ID for Production.");

        if (!Uri.TryCreate(section["CallbackUrl"]?.Trim(), UriKind.Absolute, out var callback)
            || (callback.Scheme != Uri.UriSchemeHttps && callback.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(callback.UserInfo) || !string.IsNullOrEmpty(callback.Fragment))
            throw Invalid("CallbackUrl must be an absolute HTTP(S) URL without credentials or a fragment.");

        if (mode == ZarinpalMode.Production && (callback.Scheme != Uri.UriSchemeHttps || callback.IsLoopback))
            throw Invalid("CallbackUrl must use HTTPS and a non-loopback host for Production.");
        if (mode == ZarinpalMode.Sandbox && callback.Scheme == Uri.UriSchemeHttp && !callback.IsLoopback)
            throw Invalid("CallbackUrl must use HTTPS except for loopback URLs in Sandbox.");

        return new ZarinpalSettings(mode, merchant.ToString("D"), callback);
    }

    private static InvalidOperationException Invalid(string reason)
        => new($"Invalid {SectionName} configuration: {reason}");
}
