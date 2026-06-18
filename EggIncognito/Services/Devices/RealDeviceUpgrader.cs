using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

// The real auto-update dispatcher (replaces NoopDeviceUpgrader). On each probe it asks: is the STORE ahead
// of what is installed? (store-latest = the max app version in known_versions, populated by
// VersionPollerService.) If so, and auto-update is enabled for the platform, it drives the platform updater
// to climb the device to the store version. Records the outcome. DB-gated + config-gated; never throws.
//
// Note: this is distinct from the "NEW" badge (installed ahead of our REGISTRY). Store-ahead means the
// device itself is behind Apple/Google; that is what we auto-fix. Registry-ahead stays the admin Save path.
public sealed class RealDeviceUpgrader(
    IServiceScopeFactory scopeFactory,
    DeviceUpdateConfig config,
    ILogger<RealDeviceUpgrader> logger) : IDeviceUpgrader
{
    public async Task MaybeUpgradeAsync(Device device, DeviceProbeResult result, CancellationToken ct)
    {
        if (!config.Enabled) return;
        if (!config.EnabledFor(device.Platform)) return;
        if (!result.Reachable || string.IsNullOrEmpty(result.InstalledAppVersion)) return;

        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (sp.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return;

        // Store-latest for this platform: the newest app version VersionPollerService has discovered.
        var storeLatest = await StoreAheadCheck.StoreLatestAsync(db, device.Platform, ct);
        if (!StoreAheadCheck.IsAhead(storeLatest, result.InstalledAppVersion))
            return; // device is current with (or ahead of) the store

        var updater = ResolveUpdater(device.Platform, sp);
        if (updater is null)
        {
            logger.LogInformation("device update: {Id} store {Store} > installed {Inst} but no {Plat} updater",
                device.Id, storeLatest, result.InstalledAppVersion, device.Platform);
            return;
        }

        logger.LogInformation("device update: {Id} store {Store} > installed {Inst}: starting auto-update",
            device.Id, storeLatest, result.InstalledAppVersion);
        var outcome = await updater.UpdateAsync(device, storeLatest!, ct); // IsAhead guaranteed non-null

        var status = outcome switch
        {
            { Verified: true } => "verified",
            { Started: true } => "failed",
            _ => "skipped",
        };
        var statusStore = sp.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
        if (statusStore is not null)
            await statusStore.RecordUpdateAsync(new DeviceUpdate
            {
                DeviceId = device.Id,
                AttemptedAt = DateTimeOffset.UtcNow,
                FromVersion = outcome.FromVersion,
                ToVersion = outcome.ToVersion,
                Status = status,
                Note = outcome.Note,
                TriggeredBy = "auto",
            }, ct);

        logger.LogInformation("device update: {Id} outcome started={S} verified={V} ({Note})",
            device.Id, outcome.Started, outcome.Verified, outcome.Note);
    }

    private static IDeviceUpdater? ResolveUpdater(string platform, IServiceProvider sp) => platform switch
    {
        "android" => sp.GetService(typeof(AndroidDeviceUpdater)) as IDeviceUpdater,
        "ios" => sp.GetService(typeof(IosDeviceUpdater)) as IDeviceUpdater,
        _ => null,
    };
}
