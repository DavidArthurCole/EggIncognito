using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace EggIncognito.Data.Services;

// Upserts proto versions + their .proto text. Keyed by (platform, version). Idempotent: re-ingesting
// the same build updates metadata + text rather than duplicating.
public sealed class ProtoRegistryStore(EggIncognitoDbContext db)
{
    public async Task<(ProtoVersion Row, bool Created, bool ProtoChanged)> UpsertAsync(
        string platform, string version, string package, string protoSha, string apkRef,
        DateTimeOffset detectedAt, string? detectedBy, string? protoText, CancellationToken ct = default)
    {
        var prevLatest = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == platform)
            .OrderByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

        var row = await db.ProtoVersions
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Version == version, ct);
        var created = row is null;
        if (row is null)
        {
            row = new ProtoVersion { Platform = platform, Version = version };
            db.ProtoVersions.Add(row);
        }
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

    public Task<List<ProtoVersion>> ListAsync(string? platform, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .Where(p => platform == null || p.Platform == platform)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public Task<ProtoVersion?> GetAsync(string platform, string version, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Version == version, ct);

    public Task<ProtoProto?> GetProtoAsync(int protoVersionId, CancellationToken ct = default) =>
        db.ProtoProtos.AsNoTracking().FirstOrDefaultAsync(x => x.ProtoVersionId == protoVersionId, ct);
}
