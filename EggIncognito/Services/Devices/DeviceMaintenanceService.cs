using System.Globalization;
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
    KnownVersionRecorder knownVersions,
    ILogger<DeviceMaintenanceService> logger) : BackgroundService {
    private readonly bool _syncEnabled = appConfig.GetValue("DeviceSync:Enabled", false);
    private readonly TimeSpan _noOpRetryBackoff =
        TimeSpan.FromMinutes(appConfig.GetValue("DeviceSync:RetryBackoffMinutes", 360));

#pragma warning disable IDE0028
    private readonly Dictionary<string, (string StoreLatest, DateTimeOffset At)> _lastNoOpCheck =
        new(StringComparer.OrdinalIgnoreCase);
#pragma warning restore IDE0028

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!config.Enabled || config.Devices.Count == 0) {
            logger.LogInformation("device maintenance disabled or no devices declared");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, config.IntervalMinutes)), time);
        try {
            await StoreSyncAllAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await StoreSyncAllAsync(stoppingToken);
        } catch (OperationCanceledException) {
            /* shutdown */
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

        bool force = _firstTick;
        _firstTick = false;

        await EnsureBinaryStoredAsync(sp, ct);
        await BackfillClientVersionsAsync(latest, sp, force, ct);
    }

    private bool _firstTick = true;

    private async Task RefreshStoreCatalogAsync(CancellationToken ct) {
        try {
            string appId = appConfig["DeviceUpdate:Ios:AppId"] ?? "993492744";
            string? country = appConfig["DeviceUpdate:Ios:LookupCountry"];
            string? storeLatest = await catalog.LatestVersionAsync(appId, country, ct);
            if (storeLatest is not null)
                await knownVersions.RecordAsync("ios", storeLatest, "itunes-lookup", ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device sync: store catalog refresh threw");
        }
    }

    private async Task BackfillClientVersionsAsync(
        Dictionary<string, DeviceProbe> latest, IServiceProvider sp, bool force, CancellationToken ct) {
        if (sp.GetService(typeof(GameBinaryProvider)) is not GameBinaryProvider binaries) return;
        foreach (var d in config.Devices) {
            if (!latest.TryGetValue(d.Id, out var probe)) continue;
            if (!probe.Reachable || string.IsNullOrEmpty(probe.InstalledBuild)) continue;

            try {
                int? cv = await binaries.GetClientVersionAsync(d.Platform, ct, force);
                if (cv is null && sp.GetService(typeof(DeviceCaptureManager)) is DeviceCaptureManager mgr)
                    cv = mgr.Rinfo.Latest(d.Id)?.ClientVersion;
                if (cv is not { } v) continue;
                await BackfillClientVersionAsync(sp, d, probe.InstalledBuild!, v, ct);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                logger.LogWarning(ex, "device capture: {Id} clientVersion backfill threw", d.Id);
            }
        }
    }

    private async Task BackfillClientVersionAsync(
        IServiceProvider sp, DeviceEntry d, string build, int cv, CancellationToken ct) {
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
        if (!StoreAheadCheck.IsAhead(storeLatest, probe.InstalledAppVersion)) return;

        if (_lastNoOpCheck.TryGetValue(d.Id, out var noOp)
            && string.Equals(noOp.StoreLatest, storeLatest, StringComparison.Ordinal)
            && time.GetUtcNow() - noOp.At < _noOpRetryBackoff) {
            logger.LogDebug("device sync: {Id} store {Store} already checked recently (no-op); backing off",
                d.Id, storeLatest);
            return;
        }

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
        } else if (result.Action is "up_to_date" or "manual_needed") {
            _lastNoOpCheck[d.Id] = (storeLatest!, time.GetUtcNow());
        }
    }
}
