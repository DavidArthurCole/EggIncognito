using System.Globalization;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Backfill.Sources;

// Uptodown android versions page. HTML scrape via the "scrape" client (bare clients 403). Each version
// entry carries a version label + a release date; no build/versionCode. Parse is pure + resilient: a
// markup change yields fewer/zero rows + a logged warning, never an exception into the importer.
public sealed partial class UptodownSource(IHttpClientFactory httpFactory, ILogger<UptodownSource> logger)
    : IVersionListSource
{
    private const string Url = "https://egg-inc.en.uptodown.com/android/versions";

    public string Name => "uptodown";
    public string Platform => "android";

    public async Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct)
    {
        try
        {
            var c = httpFactory.CreateClient("scrape");
            var res = await c.GetAsync(Url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("backfill: uptodown fetch {Status}", (int)res.StatusCode);
                return [];
            }
            return ParseHtml(await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: uptodown fetch failed");
            return [];
        }
    }

    // Uptodown renders each version inside a div.version (the version string) with a sibling span.date.
    // Match per list item: the version label then the nearest date span. Tolerant of attribute reorder.
    [GeneratedRegex(
        @"<div[^>]*\bclass=""[^""]*\bversion\b[^""]*""[^>]*>\s*([\d][\d.]*)\s*</div>.*?<(?:span|td)[^>]*\bclass=""[^""]*\bdate\b[^""]*""[^>]*>\s*([^<]+?)\s*</",
        RegexOptions.Singleline)]
    private static partial Regex EntryRe();

    public static IReadOnlyList<ListedVersion> ParseHtml(string html)
    {
        var result = new List<ListedVersion>();
        var seen = new HashSet<string>();
        if (string.IsNullOrEmpty(html)) return result;

        foreach (Match m in EntryRe().Matches(html))
        {
            var version = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(version) || !seen.Add(version)) continue;
            DateTimeOffset? date = null;
            if (TryParseDate(m.Groups[2].Value.Trim(), out var d)) date = d;
            result.Add(new ListedVersion(version, date, null));
        }
        return result;
    }

    private static bool TryParseDate(string raw, out DateTimeOffset date)
    {
        // Uptodown dates look like "Jan 5, 2024" or "5 Jan 2024"; fall back to generic parse.
        string[] formats = ["MMM d, yyyy", "MMM d yyyy", "d MMM yyyy", "MMMM d, yyyy", "yyyy-MM-dd"];
        if (DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out date))
            return true;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date);
    }
}
