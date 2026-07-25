using EggIncognito.Capture;

namespace EggIncognito.Services;

public sealed class CaptureSweeper(
    CaptureSessionManager manager,
    HostedCaptureOptions opts,
    TimeProvider time,
    ILogger<CaptureSweeper> logger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        try {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepOnceAsync(time.GetUtcNow());
        } catch (OperationCanceledException) {
            /* shutdown */
        }
    }

    internal async Task SweepOnceAsync(DateTimeOffset nowUtc) {
        foreach ((string key, var session) in manager.All()) {
            if (key == CaptureSessionManager.LocalKey) continue;

            bool idle = nowUtc - session.LastFlowUtc > TimeSpan.FromMinutes(opts.MaxIdleMinutes);
            bool capped = nowUtc - session.StartedUtc > TimeSpan.FromHours(opts.MaxSessionHours);

            if (session.State != CaptureState.Running) {
                if (session.State == CaptureState.Stopped && idle) {
                    manager.Remove(key);
                    logger.LogInformation("capture sweep: released stopped session {Key}", key);
                }

                continue;
            }

            if (!idle && !capped) continue;

            manager.Remove(key);
            try {
                await session.StopAsync();
            } catch (Exception ex) {
                logger.LogWarning(ex, "capture sweep: stop failed for {Key}", key);
            }

            logger.LogInformation("capture sweep: stopped {Key} ({Reason})", key, capped ? "session cap" : "idle");
        }
    }
}
