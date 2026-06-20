using System.IO.Compression;
using System.Text.Json;

namespace EggIncognito.Core.Services.Protos;

// Parses the GitHub-crawl backfill dataset (a zip of manifest.json + snapshots/*.proto) into distinct proto
// states for staging. Per the dataset SUMMARY contract: ingest proto content always, deduped by ProtoSha256;
// attach version (appVersion/build/clientVersion) ONLY when VersionConfidence="subject" (time-accurate, from
// the commit subject). "tree-scan" is a heuristic (max constant in the repo tree, can linger / mismatch) so
// its version is NOT attached - the row stages version-less for manual review, but its Confidence is carried
// so the reviewer sees the heuristic hint. For each distinct sha, the BEST record wins (subject beats
// tree-scan beats empty; earliest date breaks ties). Platform-agnostic crawl -> default platform "android".
public static class CrawlManifestReader
{
    public sealed record CrawlRecord(
        string Platform, string? AppVersion, string? Build, string? ClientVersion, string ProtoSha,
        string ProtoText, string? OriginRepo, string? OriginCommit, DateTimeOffset? OriginDate, string? Confidence);

    private sealed record ManifestRow(
        string? Repo, string? Commit, DateTimeOffset? Date, string? ProtoSha256,
        int? ClientVersion, string? AppVersion, string? Build, string? SnapshotFile, string? VersionConfidence);

    // subject (2) > tree-scan (1) > empty/other (0). Higher = preferred when deduping a sha.
    private static int ConfidenceRank(string? c) => c switch
    {
        "subject" => 2,
        "tree-scan" => 1,
        _ => 0,
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

            // Attach version only for subject-confidence; tree-scan/empty stage version-less (review-only).
            var trusted = r.VersionConfidence == "subject";
            result.Add(new CrawlRecord(
                "android",
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
