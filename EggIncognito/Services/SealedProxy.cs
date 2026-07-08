using System.Net;

namespace EggIncognito.Services;

// The "Sealed API proxy" supporter perk: an authenticated egress that hides the caller's identity
// from the downstream API. Availability is config-gated (SealedProxy:UpstreamUrl); access is
// supporter-gated and fail-closed.
public interface ISealedProxy
{
    bool IsConfigured { get; }

    // Re-checks live; a lapsed supporter loses the perk without a fresh login.
    Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default);

    // The egress client routed through the sealed upstream. Caller must have passed CanUseAsync.
    HttpClient CreateEgressClient();
}

public sealed class SealedProxyOptions
{
    // The upstream proxy URL (http/https/socks). Empty disables the perk.
    public string UpstreamUrl { get; init; } = "";
    public string? Username { get; init; }
    public string? Password { get; init; }

    public static SealedProxyOptions FromConfig(IConfiguration config) => new()
    {
        UpstreamUrl = config["SealedProxy:UpstreamUrl"] ?? "",
        Username = config["SealedProxy:Username"],
        Password = config["SealedProxy:Password"],
    };
}

public sealed class SealedProxy(
    SealedProxyOptions options, IHttpClientFactory httpFactory, ISupporterStatus supporters) : ISealedProxy
{
    public const string EgressClientName = "sealed-egress";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.UpstreamUrl);

    public async Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        if (!user.IsAuthenticated || string.IsNullOrEmpty(user.DiscordId)) return false;
        if (!user.IsSupporter) return false;
        // Live re-check: the cookie claim can be stale; a lapsed supporter must lose the perk.
        return await supporters.CheckAsync(user.DiscordId, ct);
    }

    public HttpClient CreateEgressClient() => httpFactory.CreateClient(EgressClientName);

    // Builds the WebProxy for the named egress client. Returns null when unconfigured, so the
    // HttpClient falls back to a direct connection (the perk simply does nothing).
    public static IWebProxy? BuildProxy(SealedProxyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.UpstreamUrl)) return null;
        if (!Uri.TryCreate(options.UpstreamUrl, UriKind.Absolute, out var uri)) return null;
        var proxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(options.Username))
            proxy.Credentials = new NetworkCredential(options.Username, options.Password ?? "");
        return proxy;
    }
}
