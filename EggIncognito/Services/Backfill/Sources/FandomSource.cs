using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Backfill.Sources;

// Fandom wiki Version_History via the MediaWiki parse API (JSON, no HTML scrape). Primary list source:
// it carries a changelog. Platform tagged "android": the wiki is cross-platform but appVersion is the
// shared identity, and android is the dominant platform there. Parse is pure + resilient; a layout
// change yields fewer/zero rows, never an exception.
public sealed partial class FandomSource(IHttpClientFactory httpFactory, ILogger<FandomSource> logger)
    : IVersionListSource
{
    private const string Url =
        "https://egg-inc.fandom.com/api.php?action=parse&page=Version_History&format=json&prop=wikitext";

    public string Name => "fandom";
    public string Platform => "android";

    public async Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct)
    {
        try
        {
            var c = httpFactory.CreateClient("scrape");
            var res = await c.GetAsync(Url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("backfill: fandom fetch {Status}", (int)res.StatusCode);
                return [];
            }
            var wikitext = ExtractWikitext(await res.Content.ReadAsStringAsync(ct));
            if (wikitext is null) { logger.LogWarning("backfill: fandom wikitext missing in response"); return []; }
            return ParseWikitext(wikitext);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: fandom fetch failed");
            return [];
        }
    }

    // The parse API wraps the wikitext at .parse.wikitext["*"]. Resilient: missing path returns null.
    private static string? ExtractWikitext(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("parse", out var parse)
                && parse.TryGetProperty("wikitext", out var wt)
                && wt.TryGetProperty("*", out var star) && star.ValueKind == JsonValueKind.String
                ? star.GetString()
                : null;
        }
        catch (JsonException) { return null; }
    }

    // Matches a wiki table row's leading version cell, e.g. "| 1.35.7 ||" or "|1.35.7\n". A version is
    // two-or-more dot-separated numeric groups; the first such cell on a row is the appVersion.
    [GeneratedRegex(@"^\s*\|+\s*'*\[*\s*(\d+\.\d+(?:\.\d+)*)\b", RegexOptions.Multiline)]
    private static partial Regex VersionCellRe();

    // A date cell anywhere on the same row, formats like "January 5, 2024" or "2024-01-05".
    [GeneratedRegex(
        @"\b((?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2},\s+\d{4}|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex DateRe();

    // Parses MediaWiki table rows into ListedVersions. Each physical line that opens with a version cell
    // starts a row; subsequent cells on that row (split on "||" and the newline-delimited "|" form) feed
    // the date + changelog. Resilient: anything unparseable on a row is skipped, not thrown.
    public static IReadOnlyList<ListedVersion> ParseWikitext(string wikitext)
    {
        var result = new List<ListedVersion>();
        var seen = new HashSet<string>();
        if (string.IsNullOrEmpty(wikitext)) return result;

        // Split into table rows on the wiki row separator "|-"; fall back to whole text as one block.
        var rows = Regex.Split(wikitext, @"^\s*\|-.*$", RegexOptions.Multiline);
        foreach (var row in rows)
        {
            var vm = VersionCellRe().Match(row);
            if (!vm.Success) continue;
            var version = vm.Groups[1].Value;
            if (!seen.Add(version)) continue;

            DateTimeOffset? date = null;
            var dm = DateRe().Match(row);
            if (dm.Success && TryParseDate(dm.Groups[1].Value, out var d)) date = d;

            var changelog = ExtractChangelog(row, vm.Index + vm.Length);
            result.Add(new ListedVersion(version, date, changelog));
        }
        return result;
    }

    // Everything after the version cell on the row, cleaned of wiki markup, as the changelog blob. The
    // first table cell (the version) is dropped; the rest is the human notes. Null when empty.
    private static string? ExtractChangelog(string row, int afterVersion)
    {
        var tail = row[Math.Min(afterVersion, row.Length)..];
        // Drop the date token so it does not pollute the changelog.
        tail = DateRe().Replace(tail, "");
        // Strip wiki cell separators, links, bold/italic, list bullets, templates.
        tail = Regex.Replace(tail, @"\[\[([^\]|]*\|)?([^\]]*)\]\]", "$2"); // [[a|b]] -> b
        tail = Regex.Replace(tail, @"\{\{[^}]*\}\}", ""); // {{templates}}
        tail = tail.Replace("||", " ").Replace("'''", "").Replace("''", "");
        tail = Regex.Replace(tail, @"[|*#]+", " ");
        tail = Regex.Replace(tail, @"<[^>]+>", " "); // stray html
        tail = Regex.Replace(tail, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(tail) ? null : tail;
    }

    private static bool TryParseDate(string raw, out DateTimeOffset date)
    {
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date))
            return true;
        return DateTimeOffset.TryParseExact(raw, "MMMM d, yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out date);
    }
}
