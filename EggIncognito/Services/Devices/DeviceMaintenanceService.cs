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
    IosStoreCatalog catalog,
    AndroidStoreCatalog androidCatalog,
    KnownVersionRecorder knownVersions,
    ILogger<DeviceMaintenanceService> logger) : BackgroundService {
    private static readonly TimeSpan ClimbHarvestBackoff = TimeSpan.FromMinutes(30);
    private readonly bool _syncEnabled = appConfig.GetValue("DeviceSync:Enabled", false);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(config.HarvestIntervalMinutes);
    private readonly TimeSpan _refreshSettle = TimeSpan.FromSeconds(config.HarvestSettleSeconds);
    private readonly TimeSpan _noOpRetryBackoff =
        TimeSpan.FromMinutes(appConfig.GetValue("DeviceSync:RetryBackoffMinutes", 360));
    private readonly TimeSpan _storeProbeInterval =
        TimeSpan.FromMinutes(appConfig.GetValue("DeviceSync:StoreProbeIntervalMinutes", 360));

#pragma warning disable IDE0028
    private readonly Dictionary<string, (string Build, DateTimeOffset At)> _lastClimbHarvest =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (string StoreLatest, DateTimeOffset At)> _lastNoOpCheck =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastStoreProbe =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastRefreshHarvest =
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
            while (await timer.WaitForNextTickAsync(stoppingToken)) {
                await RefreshCapturesAsync(stoppingToken);
                await StoreSyncAllAsync(stoppingToken);
            }
        } catch (OperationCanceledException) {
            /* shutdown */
        }
    }

    private async Task StartupHarvestAsync(CancellationToken ct) {
        foreach (var d in config.Devices) {
            try {
                var rinfo = await HarvestAsync(d, TimeSpan.FromSeconds(25), ct);
                logger.LogInformation("device capture: {Id} startup harvest -> {Cv}",
                    d.Id, rinfo?.ClientVersion is { } cv ? $"clientVersion {cv}" : "no rinfo (will retry on demand)");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} startup harvest threw", d.Id);
            }
        }
    }

    internal async Task RefreshCapturesAsync(CancellationToken ct) {
        if (_refreshInterval <= TimeSpan.Zero) return;
        foreach (var d in config.Devices) {
            if (_lastRefreshHarvest.TryGetValue(d.Id, out var last) && time.GetUtcNow() - last < _refreshInterval)
                continue;

            try {
                logger.LogInformation("device capture: {Id} scheduled capture refresh (every {Minutes} min)",
                    d.Id, _refreshInterval.TotalMinutes);
                var rinfo = await HarvestAsync(d, TimeSpan.FromSeconds(40), ct);
                logger.LogInformation("device capture: {Id} refresh harvest -> {Cv}",
                    d.Id, rinfo?.ClientVersion is { } cv ? $"clientVersion {cv}" : "no rinfo");
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} refresh harvest threw", d.Id);
            }
        }
    }

    private async Task<DeviceRinfo?> HarvestAsync(DeviceEntry d, TimeSpan timeout, CancellationToken ct) {
        _lastRefreshHarvest[d.Id] = time.GetUtcNow();
        var rinfo = await proxyPusher.ForceHarvestAsync(d, timeout, ct, _refreshSettle);
        if (rinfo?.ClientVersion is { } cv) await RecordClientVersionAsync(d.Id, cv, ct);
        return rinfo;
    }

    private async Task RecordClientVersionAsync(string deviceId, int clientVersion, CancellationToken ct) {
        try {
            using var scope = scopeFactory.CreateScope();
            if (scope.ServiceProvider.GetService(typeof(DeviceStateStore)) is DeviceStateStore states)
                await states.RecordClientVersionAsync(deviceId, clientVersion, ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} clientVersion persist failed", deviceId);
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

        if (_syncEnabled) await RefreshStoreCatalogAsync(ct);

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

    private async Task RefreshStoreCatalogAsync(CancellationToken ct) {
        try {
            string appId = appConfig["DeviceUpdate:Ios:AppId"] ?? "993492744";
            string? country = appConfig["DeviceUpdate:Ios:LookupCountry"];
            string? storeLatest = await catalog.LatestVersionAsync(appId, country, ct);
            if (storeLatest is not null)
                await knownVersions.RecordAsync(Platforms.Ios, storeLatest, "itunes-lookup", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device sync: ios store catalog refresh threw");
        }

        try {
            string? package = config.Devices
                .FirstOrDefault(d => Platforms.Matches(d.Platform, Platforms.Android))?.Package;
            if (package is null) return;
            string? playLatest = await androidCatalog.LatestVersionAsync(
                package, appConfig["DeviceUpdate:Android:LookupCountry"],
                appConfig["DeviceUpdate:Android:LookupLocale"] ?? "en", ct);
            if (playLatest is not null)
                await knownVersions.RecordAsync(Platforms.Android, playLatest, "play-scrape", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device sync: play store catalog refresh threw");
        }
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

            _lastClimbHarvest[d.Id] = (probe.InstalledBuild, time.GetUtcNow());

            try {
                logger.LogInformation(
                    "device capture: {Id} installed build {Build} not yet harvested (had {Prev}); launching app for fresh capture",
                    d.Id, probe.InstalledBuild, harvested?.Build ?? "none");
                var rinfo = await HarvestAsync(d, TimeSpan.FromSeconds(40), ct);
                await BackfillClientVersionAsync(sp, d, probe.InstalledBuild, rinfo, ct);
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

            (bool staged, string? stageNote) = await binaries.EnsureIosBinaryStagedAsync(ct);
            logger.LogDebug("binary store: ios stash {Result} ({Note})", staged ? "staged" : "skipped", stageNote);
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary store: ensure tick threw");
        }
    }

    private async Task StoreSyncAsync(
        Device d, DeviceProbe probe,
        IDeviceStatusStore store, EggIncognitoDbContext db, CancellationToken ct) {
        if (!_syncEnabled) return;
        if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledAppVersion)) return;

        string? storeLatest = await StoreAheadCheck.StoreLatestAsync(db, d.Platform, ct,
            crossPlatformHint: string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase));
        bool ahead = StoreAheadCheck.IsAhead(storeLatest, probe.InstalledAppVersion);
        if (ahead) {
            if (_lastNoOpCheck.TryGetValue(d.Id, out var noOp)
                && string.Equals(noOp.StoreLatest, storeLatest, StringComparison.Ordinal)
                && time.GetUtcNow() - noOp.At < _noOpRetryBackoff) {
                logger.LogDebug("device sync: {Id} store {Store} already checked recently (no-op); backing off",
                    d.Id, storeLatest);
                return;
            }
        } else {
            if (_lastStoreProbe.TryGetValue(d.Id, out var lastProbe)
                && time.GetUtcNow() - lastProbe < _storeProbeInterval) {
                return;
            }
        }

        var checker = storeCheckers.FirstOrDefault(c =>
            string.Equals(c.Platform, d.Platform, StringComparison.OrdinalIgnoreCase));
        if (checker is null) {
            logger.LogInformation("device sync: {Id} store {Store} > installed {Inst} but no {Plat} store checker",
                d.Id, storeLatest, probe.InstalledAppVersion, d.Platform);
            return;
        }

        if (ahead) {
            logger.LogInformation("device sync: {Id} store {Store} > installed {Inst}: driving on-device store",
                d.Id, storeLatest, probe.InstalledAppVersion);
        } else {
            logger.LogInformation("device sync: {Id} periodic store probe (installed {Inst}, no known newer version)",
                d.Id, probe.InstalledAppVersion);
        }

        _lastStoreProbe[d.Id] = time.GetUtcNow();
        var target = new DeviceTarget(d.Id, d.Platform, d.Target, d.Package);
        var result = await checker.CheckAndUpdateAsync(target, ct,
            msg => logger.LogInformation("device sync: {Id} {Msg}", d.Id, msg));

        if (result.Installed) {
            _lastNoOpCheck.Remove(d.Id);
            await store.RecordUpdateAsync(new DeviceUpdate {
                DeviceId = d.Id,
                AttemptedAt = DateTimeOffset.UtcNow,
                FromVersion = result.InstalledBefore,
                ToVersion = result.InstalledAfter,
                Status = "verified",
                Note = result.Note,
                TriggeredBy = "heartbeat"
            }, ct);
        } else if (result.Action is "up_to_date" or "manual_needed" && storeLatest is not null) {
            _lastNoOpCheck[d.Id] = (storeLatest, time.GetUtcNow());
        }
    }
}
