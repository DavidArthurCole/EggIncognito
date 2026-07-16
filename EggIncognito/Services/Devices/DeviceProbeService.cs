using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

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
            await StartupHarvestAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await ProbeAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

   
   
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
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return;
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

        try { await proxyPusher.PushAllAsync(config.Devices, ct); }
        catch (Exception ex) { logger.LogWarning(ex, "device capture: proxy push tick failed"); }
    }

    private async Task StoreSyncAsync(
        EggIncognito.Data.Models.Device d, EggIncognito.Data.Models.DeviceProbe probe,
        IDeviceStatusStore store, EggIncognitoDbContext db, CancellationToken ct)
    {
        if (!_syncEnabled) return;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion)) return;

        var storeLatest = await StoreAheadCheck.StoreLatestAsync(db, d.Platform, ct);
        if (!StoreAheadCheck.IsAhead(storeLatest, probe.InstalledAppVersion)) return;

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
