using System.Text.Json;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.Devices;

public sealed record PixelCanary(
    string ReleaseCandidateName, string BuildId, string? ReleaseTrackVersionName, string? FactoryImageDownloadUrl);

public static partial class PixelFingerprintParser {
    public const int CanaryLifetimeDays = 42;

    public static string? LatestVersionUrl(string versionsHtml) =>
        VersionLink().Matches(versionsHtml)
            .Select(m => m.Value.TrimEnd('"'))
            .Distinct(StringComparer.Ordinal)
            .OrderDescending(StringComparer.Ordinal)
            .FirstOrDefault();

    public static string? QprDownloadPath(string latestHtml) =>
        Href().Matches(latestHtml)
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault(h => h.Contains("download", StringComparison.Ordinal)
                                 && h.Contains("qpr", StringComparison.Ordinal));

    public static IReadOnlyList<(string Product, string Model)> Devices(string fiHtml) {
        var devices = new List<(string Product, string Model)>();
        string[] lines = fiHtml.Split('\n');
        for (int i = 0; i < lines.Length; i++) {
            var row = TableRowId().Match(lines[i]);
            if (!row.Success) continue;
            string? model = FirstCell(lines[i]) ?? (i + 1 < lines.Length ? FirstCell(lines[i + 1]) : null);
            if (model is null) continue;
            devices.Add((row.Groups[1].Value + "_beta", model));
        }

        return devices;
    }

    public static string? FlashKey(string flashHtml) {
        var body = BodyClientConfig().Match(flashHtml);
        if (!body.Success) return null;
        string rest = body.Groups[1].Value;
        int semi = rest.IndexOf(';', StringComparison.Ordinal);
        if (semi < 0) return null;
        string afterSemi = rest[(semi + 1)..];
        int nextSemi = afterSemi.IndexOf(';', StringComparison.Ordinal);
        if (nextSemi >= 0) afterSemi = afterSemi[..nextSemi];
        int amp = afterSemi.IndexOf('&', StringComparison.Ordinal);
        string key = amp >= 0 ? afterSemi[..amp] : afterSemi;
        return key.Length == 0 ? null : key;
    }

    public static PixelCanary? Canary(string buildsJson) {
        using var doc = JsonDocument.Parse(buildsJson);
        PixelCanary? last = null;
        foreach (var build in Builds(doc.RootElement)) {
            if (!IsCanary(build)) continue;
            string? id = Str(build, "releaseCandidateName");
            string? incremental = Str(build, "buildId");
            if (id is null || incremental is null) continue;
            string? track = Str(build, "releaseTrackVersionName")
                            ?? (build.TryGetProperty("previewMetadata", out var meta) ? Str(meta, "releaseTrackVersionName") : null);
            last = new PixelCanary(id, incremental, track, Str(build, "factoryImageDownloadUrl"));
        }

        return last;
    }

    private static bool IsCanary(JsonElement build) {
        if (build.TryGetProperty("canary", out var flat)) return flat.ValueKind == JsonValueKind.True;
        return build.TryGetProperty("previewMetadata", out var meta)
               && meta.ValueKind == JsonValueKind.Object
               && meta.TryGetProperty("canary", out var nested)
               && nested.ValueKind == JsonValueKind.True;
    }

    public static DateOnly Expiry(DateOnly releasedOn) => releasedOn.AddDays(CanaryLifetimeDays);

    private static IEnumerable<JsonElement> Builds(JsonElement root) {
        switch (root.ValueKind) {
            case JsonValueKind.Array:
                foreach (var e in root.EnumerateArray()) {
                    if (IsBuild(e)) {
                        yield return e;
                    } else {
                        foreach (var nested in Builds(e)) yield return nested;
                    }
                }

                break;
            case JsonValueKind.Object when root.TryGetProperty("builds", out var builds):
                foreach (var b in Builds(builds)) yield return b;
                break;
            case JsonValueKind.Object:
                foreach (var prop in root.EnumerateObject()) {
                    foreach (var nested in Builds(prop.Value)) yield return nested;
                }

                break;
        }
    }

    private static bool IsBuild(JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("buildId", out _);

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? FirstCell(string line) {
        var cell = TableCell().Match(line);
        if (!cell.Success) return null;
        string text = cell.Groups[1].Value.Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex("https://developer\\.android\\.com/about/versions/[^\"]*[0-9]\"")]
    private static partial Regex VersionLink();

    [GeneratedRegex("href=\"([^\"]*)\"")]
    private static partial Regex Href();

    [GeneratedRegex("<tr id=\"([^\"]+)\">")]
    private static partial Regex TableRowId();

    [GeneratedRegex("<td>(.*?)</td>")]
    private static partial Regex TableCell();

    [GeneratedRegex("<body data-client-config=(.*)")]
    private static partial Regex BodyClientConfig();
}
