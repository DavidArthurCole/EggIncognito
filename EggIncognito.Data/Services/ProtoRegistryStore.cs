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
}

// Upserts proto versions + their .proto text. Keyed by (platform, build): build is the monotonic
// versionCode, unique per build. appVersion is the human label, clientVersion the proto/API version
// (nullable). Idempotent: re-ingesting the same build updates metadata + text rather than duplicating.
public sealed class ProtoRegistryStore(EggIncognitoDbContext db) : IProtoBackfillStore
{
    public async Task<(ProtoVersion Row, bool Created, bool ProtoChanged)> UpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string protoSha, string apkRef, DateTimeOffset detectedAt, string? detectedBy, string? protoText,
        string source = "farm", CancellationToken ct = default)
    {
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

    // Precedence-aware upsert for the backfill importers. Metadata always fills (clientVersion, package,
    // apkRef, detectedAt when absent); proto text is written only when the caller has decided it may
    // (writeProto), so the device-extracted farm proto is never clobbered. The caller precomputes the
    // precedence decision + protoSha + messageIndex, keeping this project free of the app-side rule + index.
    public async Task BackfillUpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string? protoText, string? protoSha, string? messageIndex, bool writeProto,
        string apkRef, DateTimeOffset detectedAt, string source, CancellationToken ct = default)
    {
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
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public Task<ProtoVersion?> GetAsync(string platform, string build, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);

    public Task<ProtoProto?> GetProtoAsync(int protoVersionId, CancellationToken ct = default) =>
        db.ProtoProtos.AsNoTracking().FirstOrDefaultAsync(x => x.ProtoVersionId == protoVersionId, ct);
}
