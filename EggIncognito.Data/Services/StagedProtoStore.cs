using System.Text.Json;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Core.Services.Protos;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class StagedProtoStore(EggIncognitoDbContext db, ProtoRegistryStore registry) {
    public enum ApproveResult {
        Ok,
        Merged,
        NotFound,
        MissingBuild
    }

    public enum OfferResult {
        Staged,
        AlreadyPending,
        AlreadyInRegistry
    }

    private async Task<bool> ShaInRegistryAsync(string sha, CancellationToken ct) =>
        await db.ProtoVersions.AnyAsync(p => p.ProtoSha == sha && p.DeletedAt == null, ct);

    private async Task<bool> ShaPendingAsync(string sha, CancellationToken ct) =>
        await db.StagedProtos.AnyAsync(s => s.ProtoSha == sha && s.Status == "pending", ct);


    private static int FieldScore(string? appVersion, string? build, string? clientVersion) =>
        (string.IsNullOrWhiteSpace(appVersion) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(build) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(clientVersion) ? 0 : 1);

    private async Task<StageOutcome> StageOrReviveAsync(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string source, string? submittedBy,
        string? originRepo, string? originCommit, DateTimeOffset? originDate, string? confidence,
        CancellationToken ct) {
        if (await ShaInRegistryAsync(protoSha, ct)) return StageOutcome.AlreadyInRegistry;
        if (await ShaPendingAsync(protoSha, ct)) return StageOutcome.AlreadyPending;

        var now = DateTimeOffset.UtcNow;
        int incomingScore = FieldScore(appVersion, build, clientVersion);

        var rejected = await db.StagedProtos
            .Where(s => s.ProtoSha == protoSha && s.Status == "rejected")
            .OrderByDescending(s => s.ReviewedAt).FirstOrDefaultAsync(ct);
        if (rejected is not null) {
            if (incomingScore <= FieldScore(rejected.AppVersion, rejected.Build, rejected.ClientVersion))
                return StageOutcome.StaleRejected;
            rejected.AppVersion = string.IsNullOrWhiteSpace(rejected.AppVersion) ? appVersion : rejected.AppVersion;
            rejected.Build = string.IsNullOrWhiteSpace(rejected.Build) ? build : rejected.Build;
            rejected.ClientVersion = string.IsNullOrWhiteSpace(rejected.ClientVersion)
                ? clientVersion
                : rejected.ClientVersion;
            rejected.Platform = string.IsNullOrWhiteSpace(rejected.Platform) ? platform : rejected.Platform;
            rejected.Confidence ??= confidence;
            rejected.OriginRepo ??= originRepo;
            rejected.OriginCommit ??= originCommit;
            rejected.OriginDate ??= originDate;
            rejected.Status = "pending";
            rejected.ReviewedBy = null;
            rejected.ReviewedAt = null;
            rejected.ReviewNote = null;
            rejected.SubmittedAt = now;
            await db.SaveChangesAsync(ct);
            return StageOutcome.Revived;
        }

        db.StagedProtos.Add(new StagedProto {
            Platform = platform,
            AppVersion = appVersion,
            Build = build,
            ClientVersion = clientVersion,
            Package = package,
            ProtoSha = protoSha,
            ProtoText = protoText,
            MessageIndex = messageIndex,
            Source = source,
            Status = "pending",
            SubmittedBy = submittedBy,
            SubmittedAt = now,
            OriginRepo = originRepo,
            OriginCommit = originCommit,
            OriginDate = originDate,
            Confidence = confidence
        });
        await db.SaveChangesAsync(ct);
        return StageOutcome.Staged;
    }

    public async Task<OfferResult> OfferAsync(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string? submittedBy, string source,
        CancellationToken ct) {
        var outcome = await StageOrReviveAsync(platform, appVersion, build, clientVersion, package, protoSha,
            protoText, messageIndex, source, submittedBy, null, null, null, null, ct);
        return outcome switch {
            StageOutcome.AlreadyInRegistry => OfferResult.AlreadyInRegistry,
            StageOutcome.AlreadyPending or StageOutcome.StaleRejected => OfferResult.AlreadyPending,
            _ => OfferResult.Staged
        };
    }

    public async Task<(int staged, int skipped)> ImportCrawlAsync(
        IReadOnlyList<CrawlManifestReader.CrawlRecord> records, CancellationToken ct) {
        int staged = 0, skipped = 0;
        foreach (var r in records) {
            var norm = ProtoCanonicalForm.Normalize(r.ProtoText);
            string sha = norm.Ok ? norm.Sha! : r.ProtoSha;
            string? messageIndex = norm.Ok ? JsonSerializer.Serialize(ProtoTextIndex.Names(norm.Text!)) : null;
            var outcome = await StageOrReviveAsync(r.Platform, r.AppVersion, r.Build, r.ClientVersion, null,
                sha, r.ProtoText, messageIndex, "crawl", null, r.OriginRepo, r.OriginCommit, r.OriginDate,
                r.Confidence, ct);
            if (outcome is StageOutcome.Staged or StageOutcome.Revived) staged++;
            else skipped++;
        }

        return (staged, skipped);
    }

    public Task<List<StagedProto>> PendingAsync(CancellationToken ct) =>
        db.StagedProtos.AsNoTracking().Where(s => s.Status == "pending")
            .OrderByDescending(s => s.SubmittedAt).ToListAsync(ct);

    public Task<StagedProto?> PendingByIdAsync(int id, CancellationToken ct) =>
        db.StagedProtos.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);

    public Task<int> PendingCountAsync(CancellationToken ct) =>
        db.StagedProtos.CountAsync(s => s.Status == "pending", ct);

    public static bool FieldCompatible(string? a, string? b) {
        string left = a?.Trim() ?? "";
        string right = b?.Trim() ?? "";
        return left.Length == 0 || right.Length == 0
                                || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<(bool inRegistry, bool pending, bool knownCombination)> CheckAsync(
        string? platform, string? appVersion, string? build, string? clientVersion, string protoSha,
        CancellationToken ct) {
        bool inReg = await ShaInRegistryAsync(protoSha, ct);
        bool pending = await db.StagedProtos.AnyAsync(s => s.ProtoSha == protoSha && s.Status == "pending", ct);
        bool known = false;
        if (inReg) {
            var rows = await db.ProtoVersions.AsNoTracking()
                .Where(p => p.ProtoSha == protoSha && p.DeletedAt == null)
                .Select(p => new { p.Platform, p.AppVersion, p.Build, p.ClientVersion })
                .ToListAsync(ct);
            known = rows.Any(p => FieldCompatible(p.Platform, platform)
                                  && FieldCompatible(p.AppVersion, appVersion)
                                  && FieldCompatible(p.Build, build)
                                  && FieldCompatible(p.ClientVersion, clientVersion));
        }

        return (inReg, pending, known);
    }

    public async Task<ApproveResult> ApproveAsync(
        int id, string? platform, string? appVersion, string? build, string? clientVersion,
        string reviewedBy, CancellationToken ct) {
        var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);
        if (row is null) return ApproveResult.NotFound;

        string plat = string.IsNullOrWhiteSpace(platform) ? row.Platform : platform;
        string? appV = string.IsNullOrWhiteSpace(appVersion) ? row.AppVersion : appVersion;
        string? bld = string.IsNullOrWhiteSpace(build) ? row.Build : build;
        string? cv = string.IsNullOrWhiteSpace(clientVersion) ? row.ClientVersion : clientVersion;
        if (string.IsNullOrWhiteSpace(bld) || string.IsNullOrWhiteSpace(appV)) return ApproveResult.MissingBuild;

        var existing = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == plat && p.Build == bld, ct);
        var result = existing is null ? ApproveResult.Ok : ApproveResult.Merged;

        if (existing is null) {
            await registry.UpsertAsync(plat, appV, bld, cv, row.Package ?? "",
                row.ProtoSha, $"staged:{row.Id}", DateTimeOffset.UtcNow,
                $"staged-approve:{reviewedBy}", row.ProtoText, row.Source,
                true, ct);
        } else {
            bool hasProto = await db.ProtoProtos.AnyAsync(x => x.ProtoVersionId == existing.Id, ct);
            await registry.BackfillUpsertAsync(plat, appV, bld, cv, row.Package ?? "",
                row.ProtoText, row.ProtoSha, row.MessageIndex,
                !hasProto, $"staged:{row.Id}", DateTimeOffset.UtcNow,
                row.Source, ct);
        }

        row.Status = "approved";
        row.ReviewedBy = reviewedBy;
        row.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<bool> RejectAsync(int id, string? note, string reviewedBy, CancellationToken ct) {
        var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);
        if (row is null) return false;
        row.Status = "rejected";
        row.ReviewNote = note;
        row.ReviewedBy = reviewedBy;
        row.ReviewedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<BulkApproveResult> BulkApproveAsync(
        IReadOnlyList<ApproveItem> items, string reviewedBy, CancellationToken ct) {
        int ok = 0, skipped = 0, failed = 0;
        foreach (var it in items) {
            var r = await ApproveAsync(it.Id, it.Platform, it.AppVersion, it.Build, it.ClientVersion, reviewedBy, ct);
            switch (r) {
                case ApproveResult.Ok or ApproveResult.Merged: ok++; break;
                case ApproveResult.MissingBuild: skipped++; break;
                default: failed++; break;
            }
        }

        return new BulkApproveResult(ok, skipped, failed);
    }

    public async Task<int> BulkRejectAsync(IReadOnlyList<int> ids, string? note, string reviewedBy,
        CancellationToken ct) {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.StagedProtos.Where(s => ids.Contains(s.Id) && s.Status == "pending").ToListAsync(ct);
        foreach (var row in rows) {
            row.Status = "rejected";
            row.ReviewNote = note;
            row.ReviewedBy = reviewedBy;
            row.ReviewedAt = now;
        }

        if (rows.Count > 0) await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private enum StageOutcome {
        Staged,
        Revived,
        AlreadyPending,
        AlreadyInRegistry,
        StaleRejected
    }

    public readonly record struct ApproveItem(
        int Id,
        string? Platform,
        string? AppVersion,
        string? Build,
        string? ClientVersion);

    public readonly record struct BulkApproveResult(int Approved, int Skipped, int Failed);
}
