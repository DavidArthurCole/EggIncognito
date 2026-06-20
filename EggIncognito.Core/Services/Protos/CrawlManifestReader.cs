using System.IO.Compression;
using System.Text.Json;

namespace EggIncognito.Core.Services.Protos;

// Parses the GitHub-crawl backfill dataset (a zip of manifest.json + snapshots/*.proto) into distinct proto
// states for staging. Per the dataset SUMMARY contract: ingest proto content always, deduped by ProtoSha256;
// attach version (appVersion/build/clientVersion) only for TRUSTED confidence = "version-file" (read from a
// vendored version-const file at the commit: APP_VERSION/APP_BUILD/CLIENT_VERSION/PLATFORM, time-accurate) or
// "subject" (parsed from the commit subject). "tree-scan" is a heuristic (max constant lingering in the tree,
// can mismatch) so its version is NOT attached - the row stages version-less for manual review, its Confidence
// carried so the reviewer sees the hint. For each distinct sha, the BEST record wins (version-file > subject >
// tree-scan > empty; earliest date breaks ties). Platform comes from the manifest (IOS/ANDROID) when known,
// else defaults to "android".
public static class CrawlManifestReader
{
    public sealed record CrawlRecord(
        string Platform, string? AppVersion, string? Build, string? ClientVersion, string ProtoSha,
        string ProtoText, string? OriginRepo, string? OriginCommit, DateTimeOffset? OriginDate, string? Confidence);

    private sealed record ManifestRow(
        string? Repo, string? Commit, DateTimeOffset? Date, string? ProtoSha256,
        int? ClientVersion, string? AppVersion, string? Build, string? SnapshotFile, string? VersionConfidence,
        string? Platform);

    // version-file (3) > subject (2) > tree-scan (1) > empty/other (0). Higher = preferred when deduping a sha.
    private static int ConfidenceRank(string? c) => c switch
    {
        "version-file" => 3,
        "subject" => 2,
        "tree-scan" => 1,
        _ => 0,
    };

    // version-file + subject are time-accurate -> attach their version. tree-scan/empty stage version-less.
    private static bool IsTrusted(string? c) => c is "version-file" or "subject";

    // Manifest Platform is "IOS"/"ANDROID" (or null); the registry keys on lowercase "ios"/"android". Unknown
    // platform defaults to "android" (the registry's canonical), which the reviewer can re-key at approve.
    private static string NormalizePlatform(string? p) => p?.ToUpperInvariant() switch
    {
        "IOS" => "ios",
        "ANDROID" => "android",
        _ => "android",
    };

    public static IReadOnlyList<CrawlRecord> Read(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var manifestEntry = zip.GetEntry("manifest.json")
            ?? zip.Entries.FirstOrDefault(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null) return [];

        List<ManifestRow>? rows;
        using (var s = manifestEntry.Open())
            rows = JsonSerializer.Deserialize<List<ManifestRow>>(s,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (rows is null) return [];

        // Pick the best record per distinct ProtoSha256: prefer higher VersionConfidence, then earliest date.
        var bestPerSha = rows
            .Where(r => !string.IsNullOrEmpty(r.ProtoSha256) && !string.IsNullOrEmpty(r.SnapshotFile))
            .GroupBy(r => r.ProtoSha256!)
            .Select(g => g
                .OrderByDescending(r => ConfidenceRank(r.VersionConfidence))
                .ThenBy(r => r.Date ?? DateTimeOffset.MaxValue)
                .First());

        var result = new List<CrawlRecord>();
        foreach (var r in bestPerSha)
        {
            var snap = zip.GetEntry(r.SnapshotFile!);
            if (snap is null) continue;
            string text;
            using (var sr = new StreamReader(snap.Open())) text = sr.ReadToEnd();

            // Attach version only for trusted confidence (version-file/subject); tree-scan/empty stage
            // version-less (review-only). Platform comes from the manifest when known, else defaults android.
            var trusted = IsTrusted(r.VersionConfidence);
            result.Add(new CrawlRecord(
                NormalizePlatform(r.Platform),
                trusted ? r.AppVersion : null,
                trusted ? r.Build : null,
                trusted ? r.ClientVersion?.ToString() : null,
                // OriginDate -> UTC: commit dates carry a local offset (e.g. -08:00), but Npgsql's timestamptz
                // only accepts offset 0. Normalize so the import insert does not throw.
                r.ProtoSha256!, text, r.Repo, r.Commit, r.Date?.ToUniversalTime(),
                string.IsNullOrEmpty(r.VersionConfidence) ? null : r.VersionConfidence));
        }
        return result;
    }
}
