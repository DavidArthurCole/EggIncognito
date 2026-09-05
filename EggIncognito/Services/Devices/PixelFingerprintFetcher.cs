using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class PixelFingerprintFetcher(
    IHttpClientFactory httpFactory,
    VirtualDeviceConfig config,
    TimeProvider time,
    ILogger<PixelFingerprintFetcher> logger) {
    private const string VersionsUrl = "https://developer.android.com/about/versions";
    private const string DeveloperHost = "https://developer.android.com";
    private const string FlashUrl = "https://flash.android.com/";
    private const string BuildsUrl = "https://content-flashstation-pa.googleapis.com/v1/builds";

    public async Task<PifProfile> FetchAsync(string? preferredProduct, string securityPatch, CancellationToken ct) {
        var http = httpFactory.CreateClient(ModuleFetcher.HttpClientName);

        string versionsHtml = await http.GetStringAsync(VersionsUrl, ct);
        string latestUrl = PixelFingerprintParser.LatestVersionUrl(versionsHtml)
                           ?? throw Missing("latest Android version link");

        string latestHtml = await http.GetStringAsync(latestUrl, ct);
        string qprPath = PixelFingerprintParser.QprDownloadPath(latestHtml)
                         ?? throw Missing("QPR beta download link");

        string fiHtml = await http.GetStringAsync(DeveloperHost + qprPath, ct);
        var devices = PixelFingerprintParser.Devices(fiHtml);
        if (devices.Count == 0) throw Missing("Pixel beta device table");

        string? wanted = preferredProduct ?? config.IntegrityPixelProduct;
        bool pinned = wanted is not null && devices.Any(d => d.Product.Equals(wanted, StringComparison.Ordinal));
        (string product, string model) = pinned
            ? devices.First(d => d.Product.Equals(wanted, StringComparison.Ordinal))
            : devices[Random.Shared.Next(devices.Count)];
        string device = product.EndsWith("_beta", StringComparison.Ordinal) ? product[..^5] : product;

        string flashHtml = await http.GetStringAsync(FlashUrl, ct);
        string key = PixelFingerprintParser.FlashKey(flashHtml) ?? throw Missing("flash station client key");

        string buildsJson = await BuildsJsonAsync(http, product, key, ct);
        var canary = PixelFingerprintParser.Canary(buildsJson) ?? throw Missing($"canary build for {product}");

        DateOnly? releasedOn = await ReleasedOnAsync(http, canary.FactoryImageDownloadUrl, ct);
        DateOnly? expiry = releasedOn is { } r ? PixelFingerprintParser.Expiry(r) : null;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (expiry is { } e && e < today) {
            logger.LogWarning("pixel fingerprint: {Product} canary {Id} already expired on {Expiry}",
                product, canary.ReleaseCandidateName, e);
        } else {
            logger.LogInformation("pixel fingerprint: {Product} ({Model}) canary {Id}, Android {Track}",
                product, model, canary.ReleaseCandidateName, canary.ReleaseTrackVersionName ?? "?");
        }

        return new PifProfile(
            "Google", model, "google", product, device, "CANARY",
            canary.ReleaseCandidateName, canary.BuildId, securityPatch,
            PifProfile.LegacyInitialSdkInt, releasedOn, expiry);
    }

    private static async Task<string> BuildsJsonAsync(HttpClient http, string product, string key, CancellationToken ct) {
        string url = $"{BuildsUrl}?product={Uri.EscapeDataString(product)}&key={Uri.EscapeDataString(key)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Referrer = new Uri("https://flash.android.com");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    private async Task<DateOnly?> ReleasedOnAsync(HttpClient http, string? factoryImageUrl, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(factoryImageUrl)) return null;
        try {
            using var req = new HttpRequestMessage(HttpMethod.Head, factoryImageUrl);
            using var resp = await http.SendAsync(req, ct);
            var modified = resp.Content.Headers.LastModified;
            return modified is { } m ? DateOnly.FromDateTime(m.UtcDateTime) : null;
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException
                                         or InvalidOperationException) {
            logger.LogDebug(ex, "pixel fingerprint: release date lookup failed for {Url}", factoryImageUrl);
            return null;
        }
    }

    private static InvalidOperationException Missing(string stage) =>
        new($"pixel fingerprint fetch: could not find the {stage}");
}
