using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EggIncognito.Data.Services;

// The backfill importers' view of the store: read a row's source for a precedence decision, then
// upsert metadata + optional proto text. Lets the importer be unit-tested against a fake, DB-free.
public interface IProtoBackfillStore
{
    Task<ProtoVersion?> GetAsync(string platform, string build, CancellationToken ct = default);

    Task BackfillUpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string? protoText, string? protoSha, string? messageIndex, bool writeProto,
        string apkRef, DateTimeOffset detectedAt, string source, CancellationToken ct = default);

    // Deletes keyless/stub rows (empty Build or empty AppVersion). Returns the count removed.
    Task<int> PruneEmptyAsync(CancellationToken ct = default);
}

// Upserts proto versions + their .proto text. Keyed by (platform, build): build is the monotonic
// versionCode, unique per build. appVersion is the human label, clientVersion the proto/API version
// (nullable). Idempotent: re-ingesting the same build updates metadata + text rather than duplicating.
public sealed class ProtoRegistryStore(EggIncognitoDbContext db) : IProtoBackfillStore
{
    public async Task<(ProtoVersion Row, bool Created, bool ProtoChanged)> UpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string protoSha, string apkRef, DateTimeOffset detectedAt, string? detectedBy, string? protoText,
        string source = "farm", bool resurrect = false, CancellationToken ct = default)
    {
        // Build keys the row; a keyless event must never persist a stub row.
        if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion))
            return (new ProtoVersion { Platform = platform, Build = build, AppVersion = appVersion }, false, false);

        var prevLatest = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == platform)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

        var row = await db.ProtoVersions
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        var created = row is null;
        if (row is null)
        {
            row = new ProtoVersion { Platform = platform, Build = build };
            db.ProtoVersions.Add(row);
        }
        row.AppVersion = appVersion;
        row.ClientVersion = clientVersion;
        row.Source = source;
        row.Package = package;
        row.ProtoSha = protoSha;
        row.ApkRef = apkRef;
        row.DetectedAt = detectedAt;
        row.DetectedBy = detectedBy;
        // An authoritative re-ingest (device pull) un-hides a previously soft-deleted / merged build: a
        // real extracted proto must surface in the list + the probe, not stay suppressed by a stale delete.
        if (resurrect) { row.DeletedAt = null; row.CanonicalId = null; }
        await db.SaveChangesAsync(ct); // assigns Id

        if (!string.IsNullOrEmpty(protoText))
        {
            var pp = await db.ProtoProtos.FirstOrDefaultAsync(x => x.ProtoVersionId == row.Id, ct);
            if (pp is null) { pp = new ProtoProto { ProtoVersionId = row.Id }; db.ProtoProtos.Add(pp); }
            pp.ProtoText = protoText;
            pp.MessageIndex = JsonSerializer.Serialize(ProtoTextIndex.Names(protoText));
            await db.SaveChangesAsync(ct);
        }

        var protoChanged = prevLatest is not null && prevLatest.ProtoSha != protoSha;
        return (row, created, protoChanged);
    }

    // Precedence-aware upsert: metadata always updates; protoText written only when writeProto=true (farm proto not clobbered).
    public async Task BackfillUpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string? protoText, string? protoSha, string? messageIndex, bool writeProto,
        string apkRef, DateTimeOffset detectedAt, string source, CancellationToken ct = default)
    {
        // Build keys the row; skip keyless upserts rather than write a stub.
        if (string.IsNullOrEmpty(build)) return;

        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null)
        {
            row = new ProtoVersion { Platform = platform, Build = build, Source = source };
            db.ProtoVersions.Add(row);
        }
        row.Package = package;
        // appVersion: fill when empty, and let the authoritative farm source refresh the label.
        if (string.IsNullOrEmpty(row.AppVersion) || source == "farm") row.AppVersion = appVersion;
        row.ClientVersion ??= clientVersion; // fill only when null
        if (string.IsNullOrEmpty(row.ApkRef)) row.ApkRef = apkRef;
        if (row.DetectedAt == default) row.DetectedAt = detectedAt;
        await db.SaveChangesAsync(ct); // assigns Id

        if (writeProto && !string.IsNullOrEmpty(protoText))
        {
            row.ProtoSha = protoSha ?? "";
            var pp = await db.ProtoProtos.FirstOrDefaultAsync(x => x.ProtoVersionId == row.Id, ct);
            if (pp is null) { pp = new ProtoProto { ProtoVersionId = row.Id }; db.ProtoProtos.Add(pp); }
            pp.ProtoText = protoText;
            pp.MessageIndex = messageIndex ?? "[]";
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<Dictionary<string, int>> SourceCountsAsync(CancellationToken ct = default) =>
        await db.ProtoVersions.GroupBy(p => p.Source)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

    public Task<List<ProtoVersion>> ListAsync(string? platform, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .Where(p => platform == null || p.Platform == platform)
            // Defensive: never render keyless/stub rows even if an un-pruned one exists.
            .Where(p => p.Build != null && p.Build != "" && p.AppVersion != null && p.AppVersion != "")
            // Soft-deleted + merged-alias rows are hidden from the default list.
            .Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    // One suggested merge: a set of rows that share the combine criteria (app version + proto SHA) but are
    // separate registry entries (e.g. the iOS + Android build of the same release). Build is the per-platform
    // key; merging links the others as aliases of a chosen canonical.
    public sealed record MergeSuggestion(string AppVersion, string ProtoSha, IReadOnlyList<MergeMember> Members);
    public sealed record MergeMember(string Platform, string Build);

    // Find groups of active rows that SHOULD merge: same (AppVersion, ProtoSha), spanning >=2 distinct
    // platforms (cross-platform builds of one release). Excludes deleted/already-aliased rows + empty shas.
    public async Task<List<MergeSuggestion>> SuggestMergesAsync(CancellationToken ct = default)
    {
        var rows = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.CanonicalId == null)
            .Where(p => p.Build != null && p.Build != "" && p.AppVersion != null && p.AppVersion != "")
            .Where(p => p.ProtoSha != null && p.ProtoSha != "")
            .Select(p => new { p.Platform, p.Build, p.AppVersion, p.ProtoSha })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => new { r.AppVersion, r.ProtoSha })
            // a real suggestion spans >=2 DISTINCT platforms (an iOS+Android pair of the same release).
            .Where(g => g.Select(r => r.Platform).Distinct().Count() >= 2)
            .Select(g => new MergeSuggestion(g.Key.AppVersion, g.Key.ProtoSha,
                g.Select(r => new MergeMember(r.Platform, r.Build))
                 .OrderBy(m => m.Platform).ToList()))
            .OrderBy(s => s.AppVersion)
            .ToList();
    }

    // Soft-delete a single build: hidden from the list, kept so a re-ingest does not resurrect it.
    // Returns false when the row does not exist.
    public async Task<bool> SoftDeleteAsync(string platform, string build, CancellationToken ct = default)
    {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return false;
        row.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Restore a soft-deleted / merged row: clears DeletedAt + CanonicalId so it lists again.
    public async Task<bool> RestoreAsync(string platform, string build, CancellationToken ct = default)
    {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return false;
        row.DeletedAt = null;
        row.CanonicalId = null;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Edit human-correctable metadata on a stored build. Null args leave the field unchanged. newBuild re-keys
    // the row (build is the platform-scoped key); a no-op when equal to the current build. Returns a result
    // enum so the caller can distinguish not-found from a build-collision (target build already exists for the
    // platform).
    public enum MetadataUpdate { Ok, NotFound, BuildCollision }

    public async Task<MetadataUpdate> UpdateMetadataAsync(
        string platform, string build, string? appVersion, string? clientVersion, string? source,
        string? newBuild = null, CancellationToken ct = default)
    {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return MetadataUpdate.NotFound;

        // Re-key the build if a new (different, non-empty) one is given. Reject if another row already holds it
        // for this platform (build is unique per platform), else we would create a duplicate key.
        if (!string.IsNullOrWhiteSpace(newBuild) && newBuild != build)
        {
            var clash = await db.ProtoVersions.AnyAsync(
                p => p.Platform == platform && p.Build == newBuild && p.Id != row.Id, ct);
            if (clash) return MetadataUpdate.BuildCollision;
            row.Build = newBuild!;
        }
        if (appVersion is not null) row.AppVersion = appVersion;
        if (clientVersion is not null) row.ClientVersion = clientVersion;
        if (source is not null) row.Source = source;
        await db.SaveChangesAsync(ct);
        return MetadataUpdate.Ok;
    }

    // Merge: each alias becomes a hidden pointer to the canonical row (same schema, possibly across
    // platforms). Rejects merging a row into itself or pointing the canonical at an alias. Returns the
    // number of aliases linked.
    public async Task<int> MergeAsync(
        (string Platform, string Build) canonical, IReadOnlyList<(string Platform, string Build)> aliases,
        CancellationToken ct = default)
    {
        var canon = await db.ProtoVersions
            .FirstOrDefaultAsync(p => p.Platform == canonical.Platform && p.Build == canonical.Build, ct);
        if (canon is null) return 0;
        // The canonical must itself be a real row, not an alias of something else.
        if (canon.CanonicalId is not null) { canon.CanonicalId = null; canon.DeletedAt = null; }

        var now = DateTimeOffset.UtcNow;
        var linked = 0;
        var demotedIds = new List<int>();
        foreach (var (platform, build) in aliases)
        {
            if (platform == canonical.Platform && build == canonical.Build) continue; // skip self
            var alias = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
            if (alias is null || alias.Id == canon.Id) continue;
            demotedIds.Add(alias.Id);
            alias.CanonicalId = canon.Id;
            alias.DeletedAt = now;
            linked++;
        }

        // Re-point any rows that were aliases of a row we just demoted (or of the canonical when it was
        // itself an alias) so no CanonicalId chains past one hop. Keeps "follow CanonicalId once = root".
        var stale = await db.ProtoVersions
            .Where(p => p.CanonicalId != null
                && (demotedIds.Contains(p.CanonicalId.Value) || p.CanonicalId == canon.Id))
            .ToListAsync(ct);
        foreach (var s in stale)
        {
            if (s.Id == canon.Id) continue;
            s.CanonicalId = canon.Id;
            s.DeletedAt ??= now;
        }

        await db.SaveChangesAsync(ct);
        return linked;
    }

    public Task<int> PruneEmptyAsync(CancellationToken ct = default) =>
        db.ProtoVersions
            .Where(p => p.Build == null || p.Build == "" || p.AppVersion == null || p.AppVersion == "")
            .ExecuteDeleteAsync(ct);

    public Task<ProtoVersion?> GetAsync(string platform, string build, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);

    public Task<ProtoProto?> GetProtoAsync(int protoVersionId, CancellationToken ct = default) =>
        db.ProtoProtos.AsNoTracking().FirstOrDefaultAsync(x => x.ProtoVersionId == protoVersionId, ct);
}
