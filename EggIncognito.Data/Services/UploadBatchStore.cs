using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class UploadBatchStore(EggIncognitoDbContext db) {
    public static string InferPlatform(string fileName) =>
        fileName.EndsWith(".ipa", StringComparison.OrdinalIgnoreCase) ? "ios" : "android";

    public async Task<int> CreateAsync(string? submittedBy, IReadOnlyList<NewBatchFile> files, CancellationToken ct) {
        var batch = new UploadBatch {
            SubmittedBy = submittedBy,
            SubmittedAt = DateTimeOffset.UtcNow,
            Status = "pending",
            TotalItems = files.Count,
            ProcessedItems = 0
        };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        foreach (var f in files) {
            db.UploadBatchItems.Add(new UploadBatchItem {
                BatchId = batch.Id,
                FileName = f.FileName,
                SizeBytes = f.SizeBytes,
                Bytes = f.Bytes,
                Status = "pending",
                Platform = InferPlatform(f.FileName)
            });
        }
        await db.SaveChangesAsync(ct);
        return batch.Id;
    }

    public async Task<UploadBatchItem?> ClaimNextAsync(CancellationToken ct) {
        var item = await db.UploadBatchItems
            .Where(i => i.Status == "pending")
            .OrderBy(i => i.Id)
            .FirstOrDefaultAsync(ct);
        if (item is null) return null;
        item.Status = "processing";
        var batch = await db.UploadBatches.FirstOrDefaultAsync(b => b.Id == item.BatchId, ct);
        if (batch is not null && batch.Status == "pending") batch.Status = "processing";
        await db.SaveChangesAsync(ct);
        return item;
    }

    public async Task CompleteItemAsync(int itemId, ItemOutcome outcome, CancellationToken ct) {
        var item = await db.UploadBatchItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null) return;
        item.Status = outcome.Status;
        item.ProtoSha = outcome.ProtoSha;
        item.AppVersion = outcome.AppVersion;
        item.Build = outcome.Build;
        item.ClientVersion = outcome.ClientVersion;
        item.Diagnostics = outcome.Diagnostics;
        item.Bytes = null;
        item.ProcessedAt = DateTimeOffset.UtcNow;

        var batch = await db.UploadBatches.FirstOrDefaultAsync(b => b.Id == item.BatchId, ct);
        if (batch is not null) {
            batch.ProcessedItems += 1;
            if (batch.ProcessedItems >= batch.TotalItems) batch.Status = "done";
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ResetOrphansAsync(CancellationToken ct) {
        var orphans = await db.UploadBatchItems.Where(i => i.Status == "processing").ToListAsync(ct);
        foreach (var i in orphans) i.Status = "pending";
        if (orphans.Count > 0) await db.SaveChangesAsync(ct);
        return orphans.Count;
    }

    public async Task<BatchView?> GetAsync(int batchId, CancellationToken ct) {
        var batch = await db.UploadBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null) return null;
        var items = await db.UploadBatchItems.AsNoTracking()
            .Where(i => i.BatchId == batchId).OrderBy(i => i.Id)
            .Select(i => new ItemView(i.Id, i.FileName, i.Status, i.Platform, i.ProtoSha,
                i.AppVersion, i.Build, i.ClientVersion, i.Diagnostics))
            .ToListAsync(ct);
        return new BatchView(batch.Id, batch.Status, batch.TotalItems, batch.ProcessedItems, batch.SubmittedBy, items);
    }

    public async Task<int> CleanupAsync(DateTimeOffset olderThan, CancellationToken ct) {
        var stale = await db.UploadBatches
            .Where(b => b.Status == "done" && b.SubmittedAt < olderThan)
            .Select(b => b.Id).ToListAsync(ct);
        if (stale.Count == 0) return 0;
        var items = db.UploadBatchItems.Where(i => stale.Contains(i.BatchId));
        db.UploadBatchItems.RemoveRange(items);
        var batches = db.UploadBatches.Where(b => stale.Contains(b.Id));
        db.UploadBatches.RemoveRange(batches);
        await db.SaveChangesAsync(ct);
        return stale.Count;
    }

    public sealed record NewBatchFile(string FileName, long SizeBytes, byte[] Bytes);

    public sealed record ItemOutcome(
        string Status, string? ProtoSha, string? AppVersion, string? Build, string? ClientVersion, string? Diagnostics);

    public sealed record BatchView(
        int Id, string Status, int TotalItems, int ProcessedItems, string? SubmittedBy, IReadOnlyList<ItemView> Items);

    public sealed record ItemView(
        int Id, string FileName, string Status, string? Platform, string? ProtoSha,
        string? AppVersion, string? Build, string? ClientVersion, string? Diagnostics);
}
