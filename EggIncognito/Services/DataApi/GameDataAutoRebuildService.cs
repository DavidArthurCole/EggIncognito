namespace EggIncognito.Services.DataApi;

public sealed class GameDataAutoRebuildService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<GameDataAutoRebuildService> logger) : BackgroundService {
    private bool Enabled => config.GetValue("GameData:AutoRebuild:Enabled", true);
    private int IntervalMinutes => config.GetValue("GameData:AutoRebuild:IntervalMinutes", 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!Enabled) {
            logger.LogInformation("game data auto-rebuild disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, IntervalMinutes)), time);
        try {
            await RunOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        } catch (OperationCanceledException) {
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

        logger.LogInformation(
            "game data auto-rebuild: {Built} built, {Skipped} skipped, {Failed} failed ({Binary})",
            built, skipped, failed, binaryNote ?? "no binary");

        foreach (var r in results.Where(r => r.Status == "failed"))
            logger.LogWarning("game data auto-rebuild: {Id} failed: {Note}", r.Id, r.Note);
    }
}
