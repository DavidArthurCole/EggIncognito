using System.Text.Json;
using System.Text.RegularExpressions;
using EggIncognito.Data.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EggIncognito.Services.Backfill;

// Current-version-only, proto-less, source-tagged metadata importers for the Play + App stores. They
// confirm the latest published version per platform; they never set proto text (SourcePrecedence keeps
// store sources out of the proto column). Both run in the background, so each opens its own DI scope.

// Pure parsers, tested over canned responses.
public static partial class StoreParse
{
    // The Play store page embeds the current version near a "Current Version" / softwareVersion marker.
    // A small regex over that block is enough; we only want the version string, not full HTML parsing.
    [GeneratedRegex(@"Current Version.*?>([\d][\d.]*)<", RegexOptions.Singleline)]
    private static partial Regex PlayCurrentVersionRe();

    [GeneratedRegex(@"""softwareVersion""\s*:\s*""([\d][\d.]*)""")]
    private static partial Regex PlaySoftwareVersionRe();

    public static string? PlayVersion(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var m = PlaySoftwareVersionRe().Match(html);
        if (m.Success) return m.Groups[1].Value;
        m = PlayCurrentVersionRe().Match(html);
        return m.Success ? m.Groups[1].Value : null;
    }

    // iTunes lookup returns a results array; the first result's "version" is the current iOS version.
    public static string? AppStoreVersion(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                return null;
            return results[0].TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

// Fetches the current Android version from the public Play store page and upserts a metadata-only row.
public sealed class PlayStoreImporter(
    IHttpClientFactory httpFactory, IServiceScopeFactory scopeFactory, ILogger<PlayStoreImporter> logger)
{
    private const string Package = "com.auxbrain.egginc";
    private const string Url = "https://play.google.com/store/apps/details?id=" + Package + "&hl=en";

    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IProtoBackfillStore>();
        if (store is null) { logger.LogWarning("backfill: no store (no DB), playstore skipped"); return false; }

        var c = httpFactory.CreateClient("github"); // reuse the plain client; no auth needed
        var res = await c.GetAsync(Url, ct);
        if (!res.IsSuccessStatusCode) { logger.LogWarning("backfill: play store fetch {Status}", (int)res.StatusCode); return false; }
        var version = StoreParse.PlayVersion(await res.Content.ReadAsStringAsync(ct));
        if (version is null) { logger.LogWarning("backfill: play store version not found in page"); return false; }

        await store.BackfillUpsertAsync("android", version, version, null, Package,
            null, null, null, writeProto: false, "playstore", DateTimeOffset.UtcNow, "playstore", ct);
        logger.LogInformation("backfill: playstore current version {Version}", version);
        return true;
    }
}

// Fetches the current iOS version via the iTunes lookup API and upserts a metadata-only row. The iOS
// bundle id is config (AppStore:BundleId); unset = skip cleanly, never guess a bundle id.
public sealed class AppStoreImporter(
    IHttpClientFactory httpFactory, IConfiguration config, IServiceScopeFactory scopeFactory,
    ILogger<AppStoreImporter> logger)
{
    public async Task<bool> RunAsync(CancellationToken ct = default)
    {
        var bundleId = config["AppStore:BundleId"];
        if (string.IsNullOrWhiteSpace(bundleId))
        {
            logger.LogInformation("backfill: AppStore:BundleId unset, appstore import skipped");
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetService<IProtoBackfillStore>();
        if (store is null) { logger.LogWarning("backfill: no store (no DB), appstore skipped"); return false; }

        var c = httpFactory.CreateClient("github");
        var url = $"https://itunes.apple.com/lookup?bundleId={Uri.EscapeDataString(bundleId)}";
        var res = await c.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) { logger.LogWarning("backfill: itunes lookup {Status}", (int)res.StatusCode); return false; }
        var version = StoreParse.AppStoreVersion(await res.Content.ReadAsStringAsync(ct));
        if (version is null) { logger.LogWarning("backfill: app store version not found"); return false; }

        await store.BackfillUpsertAsync("ios", version, version, null, bundleId,
            null, null, null, writeProto: false, "appstore", DateTimeOffset.UtcNow, "appstore", ct);
        logger.LogInformation("backfill: appstore current version {Version}", version);
        return true;
    }
}
