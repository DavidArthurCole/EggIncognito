using System.Text.RegularExpressions;

namespace EggIncognito.Services.Devices;

public sealed partial class AndroidStoreCatalog(
    IHttpClientFactory httpFactory,
    ILogger<AndroidStoreCatalog> logger) {
    public async Task<string?> LatestVersionAsync(
        string package, string? country, string? locale, CancellationToken ct) {
        try {
            var client = httpFactory.CreateClient("play");
            string url = $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(package)}";
            if (!string.IsNullOrEmpty(locale)) url += $"&hl={Uri.EscapeDataString(locale)}";
            if (!string.IsNullOrEmpty(country)) url += $"&gl={Uri.EscapeDataString(country)}";

            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) {
                logger.LogWarning("play lookup for {Package} returned {Status}", package, (int)resp.StatusCode);
                return null;
            }

            string? version = ParseVersion(await resp.Content.ReadAsStringAsync(ct));
            if (version is null)
                logger.LogWarning("play lookup for {Package} found no version token on the details page", package);
            return version;
        } catch (Exception ex) {
            logger.LogWarning(ex, "play lookup for {Package} failed", package);
            return null;
        }
    }

    internal static string? ParseVersion(string html) {
        var m = VersionTokenRegex().Match(html);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"\[\[\[""(\d+(?:\.\d+)+)""\]\]")]
    private static partial Regex VersionTokenRegex();
}
