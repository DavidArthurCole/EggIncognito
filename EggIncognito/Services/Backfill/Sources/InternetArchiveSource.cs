using System.Text.Json;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Backfill.Sources;

public sealed partial class InternetArchiveSource(
    IHttpClientFactory httpFactory, ILogger<InternetArchiveSource> logger)
    : IVersionListSource
{
    public string Name => "archive";
    public string Platform => "ios";

    private const string Url =
        "https://archive.org/advancedsearch.php?q=title:(Egg Inc)+AND+mediatype:(software)"
        + "&fl[]=identifier&fl[]=title&fl[]=date&rows=200&output=json";

    public async Task<IReadOnlyList<ListedVersion>> FetchAsync(CancellationToken ct)
    {
        try
        {
            var c = httpFactory.CreateClient("scrape");
            var res = await c.GetAsync(Url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("backfill: archive search {Status}", (int)res.StatusCode);
                return [];
            }
            return ParseJson(await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "backfill: archive fetch failed");
            return [];
        }
    }

    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?")]
    private static partial Regex VersionRe();

   
    public static IReadOnlyList<ListedVersion> ParseJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var resp)
                || !resp.TryGetProperty("docs", out var docs)
                || docs.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<ListedVersion>();
            var seen = new HashSet<string>();
            foreach (var d in docs.EnumerateArray())
            {
                var title = d.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                    ? t.GetString() : null;
                var ident = d.TryGetProperty("identifier", out var i) && i.ValueKind == JsonValueKind.String
                    ? i.GetString() : null;
                var version = MatchVersion(title) ?? MatchVersion(ident);
                if (version is null || !seen.Add(version)) continue;

                DateTimeOffset? date = null;
                if (d.TryGetProperty("date", out var dt) && dt.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(dt.GetString(), out var parsed))
                    date = parsed;
                result.Add(new ListedVersion(version, date, null));
            }
            return result;
        }
        catch (JsonException) { return []; }
    }

    private static string? MatchVersion(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = VersionRe().Match(s);
        return m.Success ? m.Value : null;
    }
}
