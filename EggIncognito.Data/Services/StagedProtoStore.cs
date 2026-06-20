using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Review queue between proto sources and the live registry. Offers (public) + crawl imports (admin) land
// here as pending rows; ApproveAsync promotes to proto_versions via ProtoRegistryStore. Dedup keeps a sha
// from re-staging once it is pending, rejected, or already in the registry.
public sealed class StagedProtoStore(EggIncognitoDbContext db, ProtoRegistryStore registry)
{
    public enum OfferResult { Staged, AlreadyPending, AlreadyInRegistry }
    // Ok = created/overwrote a registry row. Merged = the build already existed, so the staged data was filled
    // into the gaps of that row (no clobber) and the staged row cleared from the queue. MissingBuild/NotFound
    // are the only non-success outcomes.
    public enum ApproveResult { Ok, Merged, NotFound, MissingBuild }

    private async Task<bool> ShaInRegistryAsync(string sha, CancellationToken ct) =>
        await db.ProtoVersions.AnyAsync(p => p.ProtoSha == sha && p.DeletedAt == null, ct);

    private async Task<bool> ShaPendingAsync(string sha, CancellationToken ct) =>
        await db.StagedProtos.AnyAsync(s => s.ProtoSha == sha && s.Status == "pending", ct);

    // How many of the three version fields a candidate would carry. Used to decide whether incoming data is
    // RICHER than a previously-rejected row (reject = "not enough data yet", not "never again").
    private static int FieldScore(string? appVersion, string? build, string? clientVersion) =>
        (string.IsNullOrWhiteSpace(appVersion) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(build) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(clientVersion) ? 0 : 1);

    // Stage a proto, or REVIVE a previously-rejected row of the same sha when the incoming data is richer
    // (more version fields filled). Dedup: a sha already pending or already in the registry is left alone.
    // A rejected sha re-surfaces to review only when it brings more data than it was rejected with; equal-or-
    // poorer data stays rejected (no churn). Returns the outcome; caller maps to its own result type.
    private enum StageOutcome { Staged, Revived, AlreadyPending, AlreadyInRegistry, StaleRejected }

    private async Task<StageOutcome> StageOrReviveAsync(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string source, string? submittedBy,
        string? originRepo, string? originCommit, DateTimeOffset? originDate, string? confidence,
        CancellationToken ct)
    {
        if (await ShaInRegistryAsync(protoSha, ct)) return StageOutcome.AlreadyInRegistry;
        if (await ShaPendingAsync(protoSha, ct)) return StageOutcome.AlreadyPending;

        var now = DateTimeOffset.UtcNow;
        var incomingScore = FieldScore(appVersion, build, clientVersion);

        // A previously-rejected row for this sha (most recent). Revive it iff the new data is strictly richer.
        var rejected = await db.StagedProtos
            .Where(s => s.ProtoSha == protoSha && s.Status == "rejected")
            .OrderByDescending(s => s.ReviewedAt).FirstOrDefaultAsync(ct);
        if (rejected is not null)
        {
            if (incomingScore <= FieldScore(rejected.AppVersion, rejected.Build, rejected.ClientVersion))
                return StageOutcome.StaleRejected; // not richer -> leave it rejected
            // Richer -> revive: fill gaps (don't clobber existing values), back to pending.
            rejected.AppVersion = string.IsNullOrWhiteSpace(rejected.AppVersion) ? appVersion : rejected.AppVersion;
            rejected.Build = string.IsNullOrWhiteSpace(rejected.Build) ? build : rejected.Build;
            rejected.ClientVersion = string.IsNullOrWhiteSpace(rejected.ClientVersion) ? clientVersion : rejected.ClientVersion;
            rejected.Platform = string.IsNullOrWhiteSpace(rejected.Platform) ? platform : rejected.Platform;
            rejected.Confidence ??= confidence;
            rejected.OriginRepo ??= originRepo;
            rejected.OriginCommit ??= originCommit;
            rejected.OriginDate ??= originDate;
            rejected.Status = "pending";
            rejected.ReviewedBy = null; rejected.ReviewedAt = null; rejected.ReviewNote = null;
            rejected.SubmittedAt = now;
            await db.SaveChangesAsync(ct);
            return StageOutcome.Revived;
        }

        db.StagedProtos.Add(new StagedProto
        {
            Platform = platform, AppVersion = appVersion, Build = build, ClientVersion = clientVersion,
            Package = package, ProtoSha = protoSha, ProtoText = protoText, MessageIndex = messageIndex,
            Source = source, Status = "pending", SubmittedBy = submittedBy, SubmittedAt = now,
            OriginRepo = originRepo, OriginCommit = originCommit, OriginDate = originDate, Confidence = confidence,
        });
        await db.SaveChangesAsync(ct);
        return StageOutcome.Staged;
    }

    public async Task<OfferResult> OfferAsync(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string? submittedBy, CancellationToken ct)
    {
        var outcome = await StageOrReviveAsync(platform, appVersion, build, clientVersion, package, protoSha,
            protoText, messageIndex, "offer", submittedBy, null, null, null, null, ct);
        return outcome switch
        {
            StageOutcome.AlreadyInRegistry => OfferResult.AlreadyInRegistry,
            // pending OR a stale (not-richer) rejected sha both read as "already submitted" to the offerer.
            StageOutcome.AlreadyPending or StageOutcome.StaleRejected => OfferResult.AlreadyPending,
            _ => OfferResult.Staged, // Staged or Revived
        };
    }

    public async Task<(int staged, int skipped)> ImportCrawlAsync(
        IReadOnlyList<EggIncognito.Core.Services.Protos.CrawlManifestReader.CrawlRecord> records, CancellationToken ct)
    {
        int staged = 0, skipped = 0;
        foreach (var r in records)
        {
            var outcome = await StageOrReviveAsync(r.Platform, r.AppVersion, r.Build, r.ClientVersion, null,
                r.ProtoSha, r.ProtoText, null, "crawl", null, r.OriginRepo, r.OriginCommit, r.OriginDate, r.Confidence, ct);
            if (outcome is StageOutcome.Staged or StageOutcome.Revived) staged++;
            else skipped++;
        }
        return (staged, skipped);
    }

    public Task<List<StagedProto>> PendingAsync(CancellationToken ct) =>
        db.StagedProtos.AsNoTracking().Where(s => s.Status == "pending")
            .OrderByDescending(s => s.SubmittedAt).ToListAsync(ct);

    public Task<int> PendingCountAsync(CancellationToken ct) =>
        db.StagedProtos.CountAsync(s => s.Status == "pending", ct);

    public async Task<(bool inRegistry, bool pending)> CheckAsync(
        string platform, string? appVersion, string protoSha, CancellationToken ct)
    {
        var inReg = await ShaInRegistryAsync(protoSha, ct);
        var pending = await db.StagedProtos.AnyAsync(s => s.ProtoSha == protoSha && s.Status == "pending", ct);
        return (inReg, pending);
    }

    public async Task<ApproveResult> ApproveAsync(
        int id, string? platform, string? appVersion, string? build, string? clientVersion,
        string reviewedBy, CancellationToken ct)
    {
        var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);
        if (row is null) return ApproveResult.NotFound;

        var plat = string.IsNullOrWhiteSpace(platform) ? row.Platform : platform!;
        var appV = string.IsNullOrWhiteSpace(appVersion) ? row.AppVersion : appVersion;
        var bld = string.IsNullOrWhiteSpace(build) ? row.Build : build;
        var cv = string.IsNullOrWhiteSpace(clientVersion) ? row.ClientVersion : clientVersion;
        if (string.IsNullOrWhiteSpace(bld) || string.IsNullOrWhiteSpace(appV)) return ApproveResult.MissingBuild;

        // Does the registry already hold this build for the platform?
        var existing = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == plat && p.Build == bld, ct);
        var result = existing is null ? ApproveResult.Ok : ApproveResult.Merged;

        if (existing is null)
        {
            // New build -> full upsert.
            await registry.UpsertAsync(plat, appV!, bld!, cv, package: row.Package ?? "",
                protoSha: row.ProtoSha, apkRef: $"staged:{row.Id}", detectedAt: DateTimeOffset.UtcNow,
                detectedBy: $"staged-approve:{reviewedBy}", protoText: row.ProtoText, source: row.Source,
                resurrect: true, ct: ct);
        }
        else
        {
            // Build already exists -> MERGE, do not clash. BackfillUpsertAsync fills only the existing row's
            // empty fields (appVersion when blank, clientVersion when null, apkRef/detectedAt when unset) and
            // writes the proto only if the existing row has none. So a staged contribution enriches an
            // incomplete row instead of being rejected back into the queue, and never clobbers good data.
            var hasProto = await db.ProtoProtos.AnyAsync(x => x.ProtoVersionId == existing.Id, ct);
            await registry.BackfillUpsertAsync(plat, appV!, bld!, cv, package: row.Package ?? "",
                protoText: row.ProtoText, protoSha: row.ProtoSha, messageIndex: row.MessageIndex,
                writeProto: !hasProto, apkRef: $"staged:{row.Id}", detectedAt: DateTimeOffset.UtcNow,
                source: row.Source, ct: ct);
        }

        row.Status = "approved"; row.ReviewedBy = reviewedBy; row.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<bool> RejectAsync(int id, string? note, string reviewedBy, CancellationToken ct)
    {
        var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);
        if (row is null) return false;
        row.Status = "rejected"; row.ReviewNote = note; row.ReviewedBy = reviewedBy;
        row.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // One item of a bulk approve: the staged id + the (possibly edited) metadata to promote with.
    public readonly record struct ApproveItem(int Id, string? Platform, string? AppVersion, string? Build, string? ClientVersion);
    public readonly record struct BulkApproveResult(int Approved, int Skipped, int Failed);

    // Bulk-approve: approve each item with its edits. Rows that cannot be approved (missing build, collision,
    // already gone) are counted as skipped/failed, never abort the batch. Returns per-outcome counts.
    public async Task<BulkApproveResult> BulkApproveAsync(
        IReadOnlyList<ApproveItem> items, string reviewedBy, CancellationToken ct)
    {
        int ok = 0, skipped = 0, failed = 0;
        foreach (var it in items)
        {
            var r = await ApproveAsync(it.Id, it.Platform, it.AppVersion, it.Build, it.ClientVersion, reviewedBy, ct);
            switch (r)
            {
                case ApproveResult.Ok or ApproveResult.Merged: ok++; break; // both promote + clear the queue
                case ApproveResult.MissingBuild: skipped++; break;          // no build -> needs a manual fill
                default: failed++; break;                                   // NotFound
            }
        }
        return new BulkApproveResult(ok, skipped, failed);
    }

    // Bulk-reject: reject each pending id (hidden + sha blocked from re-offer). Returns the count rejected.
    public async Task<int> BulkRejectAsync(IReadOnlyList<int> ids, string? note, string reviewedBy, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.StagedProtos.Where(s => ids.Contains(s.Id) && s.Status == "pending").ToListAsync(ct);
        foreach (var row in rows)
        {
            row.Status = "rejected"; row.ReviewNote = note; row.ReviewedBy = reviewedBy; row.ReviewedAt = now;
        }
        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
