namespace EggIncognito.Services.DataApi;

public sealed class EndpointCatalogAutoRefreshService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    TimeProvider time,
    ILogger<EndpointCatalogAutoRefreshService> logger) : BackgroundService {
    private bool Enabled => config.GetValue("Routes:AutoRefresh:Enabled", true);
    private int IntervalMinutes => config.GetValue("Routes:AutoRefresh:IntervalMinutes", 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!Enabled) {
            logger.LogInformation("binary route auto-refresh disabled");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, IntervalMinutes)), time);
        try {
            await RunOnceAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "binary route auto-refresh stopped by shutdown");
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var rebuilder = sp.GetRequiredService<EndpointCatalogRebuilder>();

        try {
            var result = await rebuilder.RebuildAsync(ct);
            logger.LogInformation(
                "binary route auto-refresh: {Discovered} discovered, {New} new, {Drift} drift ({Note})",
                result.Discovered, result.New, result.DriftCount, result.Note ?? "no binary");
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary route auto-refresh failed");
        }
    }
}
