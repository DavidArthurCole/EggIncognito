using EggIncognito.Data.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Protos;

public sealed class BatchUploadProcessor(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<BatchUploadProcessor> logger) : BackgroundService {
    private bool Enabled => config.GetValue("BatchUpload:Enabled", true);
    private int IntervalSeconds => config.GetValue("BatchUpload:IntervalSeconds", 5);
    private int RetentionDays => config.GetValue("BatchUpload:RetentionDays", 7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!Enabled) {
            logger.LogInformation("batch upload processor disabled");
            return;
        }

        using (var startScope = scopeFactory.CreateScope()) {
            int reset = await startScope.ServiceProvider.GetRequiredService<UploadBatchStore>().ResetOrphansAsync(stoppingToken);
            if (reset > 0) logger.LogInformation("batch upload: reset {Count} orphaned items", reset);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(2, IntervalSeconds)), time);
        try {
            await RunOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        } catch (OperationCanceledException) {
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct) {
        while (true) {
            using var scope = scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            var batches = sp.GetRequiredService<UploadBatchStore>();
            var staged = sp.GetRequiredService<StagedProtoStore>();
            var item = await batches.ClaimNextAsync(ct);
            if (item is null) break;
            try {
                UploadBatchStore.ItemOutcome outcome;
                byte[] bytes = item.Bytes ?? [];
                var extract = SniffExtract(bytes);
                if (!extract.Ok) {
                    outcome = new UploadBatchStore.ItemOutcome("failed", null, null, null, null,
                        extract.Diagnostics ?? "extraction failed");
                } else {
                    var view = await batches.GetAsync(item.BatchId, ct);
                    var offer = await staged.OfferAsync(item.Platform ?? "android", extract.AppVersion,
                        extract.Build, extract.ClientVersion?.ToString(), null, extract.ProtoSha ?? "",
                        extract.Proto ?? "", null, view?.SubmittedBy, "batch", ct);
                    outcome = Map(extract, offer);
                }
                await batches.CompleteItemAsync(item.Id, outcome, ct);
            } catch (Exception ex) {
                using var failScope = scopeFactory.CreateScope();
                await failScope.ServiceProvider.GetRequiredService<UploadBatchStore>().CompleteItemAsync(item.Id,
                    new UploadBatchStore.ItemOutcome("failed", null, null, null, null, ex.Message), ct);
                logger.LogWarning(ex, "batch upload item {Id} failed", item.Id);
            }
        }

        using var cleanupScope = scopeFactory.CreateScope();
        int purged = await cleanupScope.ServiceProvider.GetRequiredService<UploadBatchStore>().CleanupAsync(
            time.GetUtcNow().AddDays(-Math.Max(1, RetentionDays)), ct);
        if (purged > 0) {
            logger.LogInformation("batch upload: purged {Count} old batches", purged);
        }
    }

    private static DescriptorProtoCarver.ExtractResult SniffExtract(byte[] bytes) {
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        return isZip ? ArchiveProtoExtractor.Extract(bytes) : DescriptorProtoCarver.Extract(bytes);
    }

    private static UploadBatchStore.ItemOutcome Map(
        DescriptorProtoCarver.ExtractResult extract, StagedProtoStore.OfferResult offer) {
        string status = offer switch {
            StagedProtoStore.OfferResult.Staged => "staged",
            _ => "duplicate"
        };
        return new UploadBatchStore.ItemOutcome(status, extract.ProtoSha, extract.AppVersion,
            extract.Build, extract.ClientVersion?.ToString(), null);
    }

    internal static UploadBatchStore.ItemOutcome ProcessBytes(
        byte[] bytes, string _,
        Func<byte[], DescriptorProtoCarver.ExtractResult> extractZip,
        Func<byte[], DescriptorProtoCarver.ExtractResult> extractRaw,
        StagedProtoStore.OfferResult offer) {
        bool isZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        var extract = isZip ? extractZip(bytes) : extractRaw(bytes);
        if (!extract.Ok) {
            return new UploadBatchStore.ItemOutcome("failed", null, null, null, null,
                extract.Diagnostics ?? "extraction failed");
        }
        return Map(extract, offer);
    }
}
