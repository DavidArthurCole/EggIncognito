using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Backfill.Sources;

// APKPure versions page (android). Two roles: the list source (appVersion + release date) and the
// APK-download source feeding the on-demand extract path. Base URL is a ctor-overridable default so the
// .net mirror reuses the same parser. Parse is pure + resilient. DownloadApkAsync is a thin real GET,
// integration-only (not unit-tested).
public sealed partial class ApkPureSource(IHttpClientFactory httpFactory, ILogger<ApkPureSource> logger)
    : IVersionListSource
{
    private const string Package = "com.auxbrain.egginc";
    private const string DefaultBase = "https://apkpure.com";
    private readonly string _baseUrl = DefaultBase;

    public string Name => "apkpure";
    public string Platform => "android";

    private string VersionsUrl => $"{_baseUrl}/egg-inc/{Package}/versions";

    public async Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct)
    {
        try
        {
            var c = httpFactory.CreateClient("scrape");
            var res = await c.GetAsync(VersionsUrl, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("backfill: apkpure fetch {Status}", (int)res.StatusCode);
                return [];
            }
            return ParseHtml(await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: apkpure fetch failed");
            return [];
        }
    }

    // Each version list item carries a data-dt-version attribute (the appVersion) and a date cell.
    // Match the version attribute then the nearest following date span. Tolerant of attribute order.
    [GeneratedRegex(
        @"data-dt-version=""([\d][\d.]*)"".*?<span[^>]*\bclass=""[^""]*\bupdate-on\b[^""]*""[^>]*>\s*([^<]+?)\s*</span>",
        RegexOptions.Singleline)]
    private static partial Regex EntryRe();

    // Fallback: a plainer markup with <a class="version-info"> blocks holding version + date text.
    [GeneratedRegex(
        @"<div[^>]*\bclass=""[^""]*\bver-item\b[^""]*""[^>]*>.*?>\s*([\d][\d.]*)\s*<.*?<span[^>]*>\s*([A-Za-z0-9, \-]+?)\s*</span>",
        RegexOptions.Singleline)]
    private static partial Regex FallbackRe();

    public static IReadOnlyList<ListedVersion> ParseHtml(string html)
    {
        var result = new List<ListedVersion>();
        var seen = new HashSet<string>();
        if (string.IsNullOrEmpty(html)) return result;

        Collect(EntryRe().Matches(html), result, seen);
        if (result.Count == 0) Collect(FallbackRe().Matches(html), result, seen);
        return result;
    }

    private static void Collect(MatchCollection matches, List<ListedVersion> result, HashSet<string> seen)
    {
        foreach (Match m in matches)
        {
            var version = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(version) || !seen.Add(version)) continue;
            DateTimeOffset? date = null;
            if (TryParseDate(m.Groups[2].Value.Trim(), out var d)) date = d;
            result.Add(new ListedVersion(version, date, null));
        }
    }

    private static bool TryParseDate(string raw, out DateTimeOffset date)
    {
        string[] formats = ["MMM d, yyyy", "MMMM d, yyyy", "yyyy-MM-dd", "MMM d yyyy"];
        if (DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out date))
            return true;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }

    // Downloads the ARM split (config.arm64_v8a.apk) for a version, the input the proto extract actually
    // needs: the Egg Inc proto lives in lib/arm64-v8a/libegginc.so, present only in the arm split, NOT
    // base.apk (base yields only ad-network SDK protos). APKPure's /download serves an XAPK (a zip-of-apks:
    // base + per-arch + per-density + locale splits) for split-APK apps; we unzip and pull the arm split
    // out. Returns null when the download is a single base APK (no arm split inside) or not a bundle.
    public async Task<byte[]?> DownloadArmSplitAsync(string appVersion, CancellationToken ct = default)
    {
        var bytes = await DownloadApkAsync(appVersion, ct);
        return bytes is null ? null : ExtractArmSplit(bytes);
    }

    // Pulls the arm64_v8a split apk bytes out of an APKPure XAPK (zip-of-apks). Pure + testable: no
    // network. Returns null if the blob is not a zip-of-apks, or is a single base APK with no arm split.
    // Matches both spellings: APKPure names it config.arm64_v8a.apk, an adb device-pull names it
    // split_config.arm64_v8a.apk. The base com.auxbrain.egginc.apk is excluded.
    public static byte[]? ExtractArmSplit(byte[] downloaded)
    {
        if (downloaded is null || downloaded.Length == 0) return null;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(downloaded, writable: false), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var name = entry.Name; // file name only, no zip path prefix
                if (name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("arm64_v8a", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals($"{Package}.apk", StringComparison.OrdinalIgnoreCase))
                {
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            return null;
        }
        catch (InvalidDataException)
        {
            // Not a zip (a single APK is itself a zip, but a non-archive blob throws here); treat as no
            // arm split. A bare base.apk would parse as a zip but carry no arm64_v8a entry, also null.
            return null;
        }
    }

    // Downloads the APK bytes for a given appVersion from APKPure's download endpoint. Real, thin, and
    // integration-only (the extract path uses it on the frame; not unit-tested). Returns null on failure.
    public async Task<byte[]?> DownloadApkAsync(string appVersion, CancellationToken ct = default)
    {
        try
        {
            var c = httpFactory.CreateClient("scrape");
            var url = $"{_baseUrl}/egg-inc/{Package}/download/{Uri.EscapeDataString(appVersion)}";
            var res = await c.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("backfill: apkpure apk download {Version} {Status}", appVersion, (int)res.StatusCode);
                return null;
            }
            return await res.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: apkpure apk download {Version} failed", appVersion);
            return null;
        }
    }
}
