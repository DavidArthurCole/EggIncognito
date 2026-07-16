using System.Globalization;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Backfill.Sources;

public interface IApkDownloader
{
    Task<byte[]?> DownloadApkAsync(string appVersion, CancellationToken ct = default);
}
public sealed partial class ApkPureSource(IHttpClientFactory httpFactory, ILogger<ApkPureSource> logger)
    : IVersionListSource, IApkDownloader
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

   
    [GeneratedRegex(
        @"data-dt-version=""([\d][\d.]*)"".*?<span[^>]*\bclass=""[^""]*\bupdate-on\b[^""]*""[^>]*>\s*([^<]+?)\s*</span>",
        RegexOptions.Singleline)]
    private static partial Regex EntryRe();

   
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

   
   
    public async Task<byte[]?> DownloadArmSplitAsync(string appVersion, CancellationToken ct = default)
    {
        var bytes = await DownloadApkAsync(appVersion, ct);
        return bytes is null ? null : ExtractArmSplit(bytes);
    }

    public static byte[]? ExtractArmSplit(byte[] downloaded) =>
        EggIncognito.Services.ProtoExtract.ApkPureDownloader.ExtractArmSplit(downloaded);

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
