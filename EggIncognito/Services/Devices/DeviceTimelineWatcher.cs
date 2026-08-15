namespace EggIncognito.Services.Devices;

public sealed class DeviceTimelineWatcher(DeviceTimelineCache cache, ILogger<DeviceTimelineWatcher> logger)
    : BackgroundService {
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                await cache.RefreshMovedAsync(stoppingToken);
            } catch (OperationCanceledException) {
                return;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device timeline watermark poll failed");
            }
        }
    }
}
