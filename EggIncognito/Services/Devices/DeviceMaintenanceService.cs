using System.Globalization;
using EggIncognito.Capture;
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
    private static readonly TimeSpan ClimbHarvestBackoff = TimeSpan.FromMinutes(30);
    private readonly bool _syncEnabled = appConfig.GetValue("DeviceSync:Enabled", false);

#pragma warning disable IDE0028
    private readonly Dictionary<string, (string Build, DateTimeOffset At)> _lastClimbHarvest =
        new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

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

        await HarvestClimbedDevicesAsync(latest, sp, ct);
        await EnsureBinaryStoredAsync(sp, ct);
    }

    private async Task HarvestClimbedDevicesAsync(
        Dictionary<string, DeviceProbe> latest, IServiceProvider sp, CancellationToken ct) {
        foreach (var d in config.Devices) {
            if (!latest.TryGetValue(d.Id, out var probe)) continue;
            if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledBuild)) continue;
            var harvested = proxyPusher.LastRinfo(d.Id);
            if (harvested is not null &&
                string.Equals(harvested.Build, probe.InstalledBuild, StringComparison.Ordinal)) {
                continue;
            }

            if (_lastClimbHarvest.TryGetValue(d.Id, out var last)
                && string.Equals(last.Build, probe.InstalledBuild, StringComparison.Ordinal)
                && time.GetUtcNow() - last.At < ClimbHarvestBackoff) {
                continue;
            }

            _lastClimbHarvest[d.Id] = (probe.InstalledBuild!, time.GetUtcNow());

            try {
                logger.LogInformation(
                    "device capture: {Id} installed build {Build} not yet harvested (had {Prev}); launching app for fresh capture",
                    d.Id, probe.InstalledBuild, harvested?.Build ?? "none");
                var rinfo = await proxyPusher.ForceHarvestAsync(d, TimeSpan.FromSeconds(40), ct);
                await BackfillClientVersionAsync(sp, d, probe.InstalledBuild!, rinfo, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} climb harvest threw", d.Id);
            }
        }
    }

    private async Task BackfillClientVersionAsync(
        IServiceProvider sp, DeviceEntry d, string build, DeviceRinfo? rinfo, CancellationToken ct) {
        if (rinfo?.ClientVersion is not { } cv) return;
        if (sp.GetService(typeof(ProtoRegistryStore)) is not ProtoRegistryStore registry) return;

        var res = await registry.UpdateMetadataAsync(d.Platform, build, null,
            cv.ToString(CultureInfo.InvariantCulture), null, ct: ct);
        logger.LogInformation("device capture: {Id} backfill clientVersion {Cv} onto {Plat} build {Build} -> {Res}",
            d.Id, cv, d.Platform, build, res);
    }

    private async Task EnsureBinaryStoredAsync(IServiceProvider sp, CancellationToken ct) {
        if (sp.GetService(typeof(GameBinaryProvider)) is not GameBinaryProvider binaries) return;
        try {
            foreach ((string platform, string status, string? version, string? note) in
                     await binaries.EnsureAllVersionsStoredAsync(ct)) {
                switch (status) {
                    case "pulled":
                        logger.LogInformation("binary store: {Platform} pulled and stored {Version}; {Note}", platform,
                            version, note);
                        break;
                    case "pull-failed":
                    case "store-error":
                        logger.LogWarning("binary store: {Platform} ensure {Version} failed ({Status}): {Note}",
                            platform, version, status, note);
                        break;
                    default:
                        logger.LogDebug("binary store: {Platform} {Status} {Version} {Note}", platform, status,
                            version ?? "?", note);
                        break;
                }
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary store: ensure tick threw");
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
        var target = new DeviceTarget(d.Id, d.Platform, d.Target, d.Package);
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
