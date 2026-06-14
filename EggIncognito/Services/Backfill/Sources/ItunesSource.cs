using System.Globalization;
using System.Text.Json;

namespace EggIncognito.Services.Backfill.Sources;

// iTunes lookup (ios, current version only). Real iOS history arrives later via the jailbroken farm.
// AppStore:BundleId config overrides; unset = fall back to the known Egg Inc bundle id so the trigger
// works out of the box. Parse is pure + resilient.
public sealed class ItunesSource(
    IHttpClientFactory httpFactory, IConfiguration config, ILogger<ItunesSource> logger)
    : IVersionListSource
{
    public string Name => "itunes";
    public string Platform => "ios";

    // Known Egg Inc bundle id; the iTunes lookup works against it with no config.
    private const string DefaultBundleId = "com.auxbrain.egginc";

    public async Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct)
    {
        var bundleId = config["AppStore:BundleId"];
        if (string.IsNullOrWhiteSpace(bundleId)) bundleId = DefaultBundleId;
        try
        {
            var c = httpFactory.CreateClient("scrape");
            var url = $"https://itunes.apple.com/lookup?bundleId={Uri.EscapeDataString(bundleId)}";
            var res = await c.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("backfill: itunes lookup {Status}", (int)res.StatusCode);
                return [];
            }
            return ParseJson(await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: itunes fetch failed");
            return [];
        }
    }

    // results[0].version (+ currentVersionReleaseDate, releaseNotes when present) as a single-element
    // list. Resilient: a missing/oddly-shaped payload yields an empty list, never throws.
    public static IReadOnlyList<ListedVersion> ParseJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return [];
            var first = results[0];
            if (!first.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String)
                return [];
            var version = v.GetString()!;
            DateTimeOffset? date = null;
            if (first.TryGetProperty("currentVersionReleaseDate", out var rd)
                && rd.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(rd.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var d))
                date = d;
            var changelog = first.TryGetProperty("releaseNotes", out var rn)
                && rn.ValueKind == JsonValueKind.String ? rn.GetString() : null;
            return [new ListedVersion(version, date, changelog)];
        }
        catch (JsonException) { return []; }
    }
}
