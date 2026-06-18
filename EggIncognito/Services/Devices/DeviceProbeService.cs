using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

// Background poller for plugged-in devices. Copies VersionPollerService's PeriodicTimer loop: first tick
// shortly after boot, then on the configured interval. DB-gated; logs every probe via DeviceProbeRunner.
public sealed class DeviceProbeService(
    IServiceScopeFactory scopeFactory,
    DeviceConfig config,
    IProcessRunner runner,
    TimeProvider time,
    DeviceProxyPusher proxyPusher,
    ILogger<DeviceProbeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.Enabled || config.Devices.Count == 0)
        {
            logger.LogInformation("device poller disabled or no devices declared");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes)), time);
        try
        {
            await ProbeAllAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProbeAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    internal async Task ProbeAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return; // no DB
        var db = (EggIncognitoDbContext)sp.GetRequiredService(typeof(EggIncognitoDbContext));
        var upgrader = (IDeviceUpgrader)sp.GetRequiredService(typeof(IDeviceUpgrader));

        foreach (var d in await store.EnabledDevicesAsync(ct))
        {
            try { await DeviceProbeRunner.ProbeOneAsync(d, "poll", runner, store, db, upgrader, logger, time, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "device probe: {Id} threw", d.Id); }
        }

        // Self-healing proxy push: re-point each declared device at its capture listener every tick, so a
        // device reboot or server restart re-applies the setting without manual steps. No-op when capture is
        // disabled or the host IP cannot be resolved.
        try { await proxyPusher.PushAllAsync(config.Devices, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "device capture: proxy push tick failed"); }
    }
}
