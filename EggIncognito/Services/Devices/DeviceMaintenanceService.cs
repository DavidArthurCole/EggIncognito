using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed class DeviceMaintenanceService(
    IServiceScopeFactory scopeFactory,
    DeviceConfig config,
    TimeProvider time,
    DeviceProxyPusher proxyPusher,
    IEnumerable<IDeviceStoreChecker> storeCheckers,
    IConfiguration appConfig,
    ILogger<DeviceMaintenanceService> logger) : BackgroundService {
    private readonly bool _syncEnabled = appConfig.GetValue("DeviceSync:Enabled", false);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled || config.Devices.Count == 0) {
            logger.LogInformation("device maintenance disabled or no devices declared");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes)), time);
        try {
            await StartupHarvestAsync(stoppingToken);
            await StoreSyncAllAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await StoreSyncAllAsync(stoppingToken);
        } catch (OperationCanceledException) {
            /* shutdown */
        }
    }


    private async Task StartupHarvestAsync(CancellationToken ct) {
        foreach (var d in config.Devices) {
            try {
                var rinfo = await proxyPusher.ForceHarvestAsync(d, TimeSpan.FromSeconds(25), ct);
                logger.LogInformation("device capture: {Id} startup harvest -> {Cv}",
                    d.Id, rinfo?.ClientVersion is { } cv ? $"clientVersion {cv}" : "no rinfo (will retry on demand)");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} startup harvest threw", d.Id);
            }
        }
    }

    internal async Task StoreSyncAllAsync(CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(IDeviceStatusStore)) is not IDeviceStatusStore store) return;
        var db = (EggIncognitoDbContext)sp.GetRequiredService(typeof(EggIncognitoDbContext));

        var latest = (await store.LatestPerDeviceAsync(ct))
            .GroupBy(p => p.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var d in await store.EnabledDevicesAsync(ct)) {
            if (!latest.TryGetValue(d.Id, out var probe)) continue;
            try {
                await StoreSyncAsync(d, probe, store, db, ct);
            } catch (Exception ex) {
                logger.LogWarning(ex, "device sync: {Id} threw", d.Id);
            }
        }

        try {
            await proxyPusher.PushAllAsync(config.Devices, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device capture: proxy push tick failed");
        }
    }

    private async Task StoreSyncAsync(
        Device d, DeviceProbe probe,
        IDeviceStatusStore store, EggIncognitoDbContext db, CancellationToken ct) {
        if (!_syncEnabled) return;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion)) return;

        string? storeLatest = await StoreAheadCheck.StoreLatestAsync(db, d.Platform, ct);
        if (!StoreAheadCheck.IsAhead(storeLatest, probe.InstalledAppVersion)) return;

        var checker = storeCheckers.FirstOrDefault(c =>
            string.Equals(c.Platform, d.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null) {
            logger.LogInformation("device sync: {Id} store {Store} > installed {Inst} but no {Plat} store checker",
                d.Id, storeLatest, probe.InstalledAppVersion, d.Platform);
            return;
        }

        logger.LogInformation("device sync: {Id} store {Store} > installed {Inst}: driving on-device store",
            d.Id, storeLatest, probe.InstalledAppVersion);
        var target = new DeviceStoreTarget(d.Id, d.Platform, d.Target, d.Package);
        var result = await checker.CheckAndUpdateAsync(target, ct,
            msg => logger.LogInformation("device sync: {Id} {Msg}", d.Id, msg));

        if (result.Installed) {
            await store.RecordUpdateAsync(new DeviceUpdate {
                DeviceId = d.Id,
                AttemptedAt = DateTimeOffset.UtcNow,
                FromVersion = result.InstalledBefore,
                ToVersion = result.InstalledAfter,
                Status = "verified",
                Note = result.Note,
                TriggeredBy = "heartbeat"
            }, ct);
        }
    }
}
