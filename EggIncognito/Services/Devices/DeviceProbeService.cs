using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

// Background heartbeat for the plugged-in slave devices. Copies VersionPollerService's PeriodicTimer loop:
// first tick shortly after boot, then on the configured interval. Per tick, per device, it:
//   1. probes the installed version + classifies it vs the registry (the NEW badge), records a probe row;
//   2. STORE-SYNC: if the device's own store is ahead of what is installed, tells the device to update
//      itself via its IDeviceStoreChecker (Android drives on-device Play over adb, iOS fires the eggupdate
//      tweak over ssh). The server is the manager: it only drives a device store when the store-latest it
//      already knows (VersionPollerService/iTunes/Play -> known_versions) is genuinely ahead of installed.
//      The server never downloads a package; the device's own store does the install.
//   3. re-points the device's proxy at its capture listener (self-healing).
// DB-gated; the store-sync is gated by DeviceSync:Enabled (default off) so a misconfigured host never drives
// a device install. Same IDeviceStoreChecker the manual check-update button uses; button + heartbeat are
// the one mechanism, two triggers.
public sealed class DeviceProbeService(
    IServiceScopeFactory scopeFactory,
    DeviceConfig config,
    IProcessRunner runner,
    TimeProvider time,
    DeviceProxyPusher proxyPusher,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    Microsoft.Extensions.Configuration.IConfiguration appConfig,
    ILogger<DeviceProbeService> logger) : BackgroundService
{
    private readonly bool _syncEnabled = appConfig.GetValue("DeviceSync:Enabled", false);

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
            // On launch, run each device through one app cycle so clientVersion is captured immediately - the
            // panel shows a populated cv from boot instead of looking broken until someone taps the play button.
            await StartupHarvestAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProbeAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    // One app cycle per device at launch so clientVersion is captured up front (panel shows a real cv from
    // boot, not an empty/broken-looking row). Best-effort + logged; a device that is off or unreachable just
    // logs and is picked up by the next manual/heartbeat trigger. Capture must be enabled for this to matter.
    private async Task StartupHarvestAsync(CancellationToken ct)
    {
        foreach (var d in config.Devices)
        {
            try
            {
                var rinfo = await proxyPusher.ForceHarvestAsync(d, TimeSpan.FromSeconds(25), ct);
                logger.LogInformation("device capture: {Id} startup harvest -> {Cv}",
                    d.Id, rinfo?.ClientVersion is { } cv ? $"clientVersion {cv}" : "no rinfo (will retry on demand)");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "device capture: {Id} startup harvest threw", d.Id); }
        }
    }

    internal async Task ProbeAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return; // no DB
        var db = (EggIncognitoDbContext)sp.GetRequiredService(typeof(EggIncognitoDbContext));

        foreach (var d in await store.EnabledDevicesAsync(ct))
        {
            try
            {
                var row = await DeviceProbeRunner.ProbeOneAsync(d, "poll", runner, store, db, logger, time, ct);
                await StoreSyncAsync(d, row, store, db, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "device probe: {Id} threw", d.Id); }
        }

        // Self-healing proxy push: re-point each declared device at its capture listener every tick, so a
        // device reboot or server restart re-applies the setting without manual steps. No-op when capture is
        // disabled or the host IP cannot be resolved.
        try { await proxyPusher.PushAllAsync(config.Devices, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "device capture: proxy push tick failed"); }
    }

    // Heartbeat store-sync: tell the device to update itself via its own store, but ONLY when the store-latest
    // we already know is ahead of what is installed (server-as-manager: no wasted store drives). Gated by
    // DeviceSync:Enabled. The actual drive + install is the device's own store (adb Play / iOS tweak), via the
    // same IDeviceStoreChecker the manual button uses. Best-effort; a failure is logged, never thrown.
    private async Task StoreSyncAsync(
        EggIncognito.Data.Models.Device d, EggIncognito.Data.Models.DeviceProbe probe,
        IDeviceStatusStore store, EggIncognitoDbContext db, CancellationToken ct)
    {
        if (!_syncEnabled) return;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion)) return;

        var storeLatest = await StoreAheadCheck.StoreLatestAsync(db, d.Platform, ct);
        if (!StoreAheadCheck.IsAhead(storeLatest, probe.InstalledAppVersion)) return; // current with / ahead of store

        var checker = storeCheckers.FirstOrDefault(c =>
            string.Equals(c.Platform, d.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null)
        {
            logger.LogInformation("device sync: {Id} store {Store} > installed {Inst} but no {Plat} store checker",
                d.Id, storeLatest, probe.InstalledAppVersion, d.Platform);
            return;
        }

        logger.LogInformation("device sync: {Id} store {Store} > installed {Inst}: driving on-device store",
            d.Id, storeLatest, probe.InstalledAppVersion);
        var target = new DeviceStoreTarget(d.Id, d.Platform, d.Target, d.Package);
        var result = await checker.CheckAndUpdateAsync(target, ct,
            msg => logger.LogInformation("device sync: {Id} {Msg}", d.Id, msg));

        if (result.Installed)
            await store.RecordUpdateAsync(new EggIncognito.Data.Models.DeviceUpdate
            {
                DeviceId = d.Id, AttemptedAt = DateTimeOffset.UtcNow,
                FromVersion = result.InstalledBefore, ToVersion = result.InstalledAfter,
                Status = "verified", Note = result.Note, TriggeredBy = "heartbeat",
            }, ct);
    }
}
