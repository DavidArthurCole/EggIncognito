using System.Net;

namespace EggIncognito.Services;

public interface ISealedProxy {
    bool IsConfigured { get; }


    Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default);


    HttpClient CreateEgressClient();
}

public sealed class SealedProxyOptions {
    public string UpstreamUrl { get; init; } = "";
    public string? Username { get; init; }
    public string? Password { get; init; }

    public static SealedProxyOptions FromConfig(IConfiguration config) => new() {
        UpstreamUrl = config["SealedProxy:UpstreamUrl"] ?? "",
        Username = config["SealedProxy:Username"],
        Password = config["SealedProxy:Password"]
    };
}

public sealed class SealedProxy(
    SealedProxyOptions options,
    IHttpClientFactory httpFactory,
    ISupporterStatus supporters) : ISealedProxy {
    public const string EgressClientName = "sealed-egress";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.UpstreamUrl);

    public async Task<bool> CanUseAsync(ICurrentUser user, CancellationToken ct = default) {
        if (!IsConfigured) return false;
        return user.IsAuthenticated && !string.IsNullOrEmpty(user.DiscordId) && user.IsSupporter &&
               await supporters.CheckAsync(user.DiscordId, ct);
    }

    public HttpClient CreateEgressClient() => httpFactory.CreateClient(EgressClientName);


    public static IWebProxy? BuildProxy(SealedProxyOptions options) {
        if (string.IsNullOrWhiteSpace(options.UpstreamUrl)) return null;
        if (!Uri.TryCreate(options.UpstreamUrl, UriKind.Absolute, out var uri)) return null;
        var proxy = new WebProxy(uri);
        if (!string.IsNullOrEmpty(options.Username))
            proxy.Credentials = new NetworkCredential(options.Username, options.Password ?? "");
        return proxy;
    }
}
