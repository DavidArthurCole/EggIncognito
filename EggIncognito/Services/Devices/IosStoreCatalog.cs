using System.Text.Json;

namespace EggIncognito.Services.Devices;

public sealed class IosStoreCatalog(
    IHttpClientFactory httpFactory,
    ILogger<IosStoreCatalog> logger) {
    public async Task<string?> LatestVersionAsync(string appId, string? country, CancellationToken ct) {
        try {
            var client = httpFactory.CreateClient("itunes");
            string url = $"https://itunes.apple.com/lookup?id={appId}";
            if (!string.IsNullOrEmpty(country)) url += $"&country={country}";
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) {
                logger.LogWarning("itunes lookup for {AppId} returned {Status}", appId, (int)resp.StatusCode);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("resultCount", out var count) || count.GetInt32() < 1) return null;
            return doc.RootElement.GetProperty("results")[0].GetProperty("version").GetString();
        } catch (Exception ex) {
            logger.LogWarning(ex, "itunes lookup for {AppId} failed", appId);
            return null;
        }
    }
}
