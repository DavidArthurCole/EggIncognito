using EggIncognito.Capture;

namespace EggIncognito.Services;

// Reaps hosted capture sessions: stop + remove after MaxIdleMinutes without a flow or MaxSessionHours
// total. The local session is never swept. Registered only when hosted capture is enabled.
public sealed class CaptureSweeper(
    CaptureSessionManager manager,
    HostedCaptureOptions opts,
    TimeProvider time,
    ILogger<CaptureSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepOnceAsync(time.GetUtcNow());
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    internal async Task SweepOnceAsync(DateTimeOffset nowUtc)
    {
        foreach (var (key, session) in manager.All())
        {
            if (key == CaptureSessionManager.LocalKey) continue;

            var idle = nowUtc - session.LastFlowUtc > TimeSpan.FromMinutes(opts.MaxIdleMinutes);
            var capped = nowUtc - session.StartedUtc > TimeSpan.FromHours(opts.MaxSessionHours);

            if (session.State != CaptureState.Running)
            {
                // A user-stopped session still holds a pool slot; release it once the idle window
                // passes so abandoned sessions cannot pin capacity.
                if (session.State == CaptureState.Stopped && idle)
                {
                    manager.Remove(key);
                    logger.LogInformation("capture sweep: released stopped session {Key}", key);
                }
                continue;
            }

            if (!idle && !capped) continue;
            // Remove from the manager BEFORE stopping, so a concurrent GetOrCreate (e.g. the user clicking
            // Start mid-sweep) gets a fresh session instead of this one being torn down.
            manager.Remove(key);
            try { await session.StopAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "capture sweep: stop failed for {Key}", key); }
            logger.LogInformation("capture sweep: stopped {Key} ({Reason})", key, capped ? "session cap" : "idle");
        }
    }
}
