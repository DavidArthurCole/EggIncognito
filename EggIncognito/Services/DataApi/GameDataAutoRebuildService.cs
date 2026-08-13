namespace EggIncognito.Services.DataApi;

public sealed class GameDataAutoRebuildService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<GameDataAutoRebuildService> logger) : BackgroundService {
    private bool Enabled => config.GetValue("GameData:AutoRebuild:Enabled", true);
    private int IntervalMinutes => config.GetValue("GameData:AutoRebuild:IntervalMinutes", 5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!Enabled) {
            logger.LogInformation("game data auto-rebuild disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes)), time);
        try {
            await RunOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "game data auto-rebuild stopped by shutdown");
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var rebuilder = sp.GetRequiredService<GameDataRebuilder>();

        (var results, string? binaryNote) = await rebuilder.RebuildAsync(ct);

        int built = results.Count(r => r.Status == "built");
        int failed = results.Count(r => r.Status == "failed");
        int skipped = results.Count(r => r.Status == "skipped");
        int current = results.Count(r => r.Status == "current");

        if (built + failed + skipped == 0) return;

        logger.LogInformation(
            "game data auto-rebuild: {Built} built, {Current} current, {Skipped} skipped, {Failed} failed ({Binary})",
            built, current, skipped, failed, binaryNote ?? "no binary");

        foreach (var r in results.Where(r => r.Status == "failed"))
            logger.LogWarning("game data auto-rebuild: {Id} failed: {Note}", r.Id, r.Note);
    }
}
