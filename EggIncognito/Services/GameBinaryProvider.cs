using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services;

// Pulls the decrypted egginc iOS Mach-O off the asset-source device and caches it, so the decomp constant
// extractor can read game behavior out of it without the binary ever living in the repo (no-game-assets rule).
// Mirrors DeviceMeshProvider's device resolution + cache. iOS-only in v1: the extractor's slide/section logic
// is Mach-O; the android .so is ELF and is deferred.
public sealed class GameBinaryProvider(
    IServiceProvider services, MeshAssetCache cache, IProcessRunner runner, IConfiguration config,
    ILogger<GameBinaryProvider> logger)
{
    private const string CacheKind = "binary";
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;

    public async Task<(bool Ok, byte[]? Bytes, string? Diagnostics)> GetBinaryAsync(string? deviceId, CancellationToken ct)
    {
        var store = Store;
        if (store is null) return (false, null, "device subsystem unavailable");

        var device = await ResolveDeviceAsync(store, deviceId, ct);
        var platform = device?.Platform ?? "ios";

        if (cache.TryGet(CacheKind, platform) is { } cached) return (true, cached, null);

        if (device is null) return (false, null, "no asset-source device available");
        if (device.Platform != "ios")
            return (false, null, "v1 extracts the iOS Mach-O only; android .so deferred");

        var cfg = config.GetSection("DeviceUpdate").GetSection("Ios");
        var key = cfg["SshKeyPath"];
        if (string.IsNullOrEmpty(key)) return (false, null, "ios pull needs DeviceUpdate:Ios:SshKeyPath");
        var host = string.IsNullOrEmpty(cfg["SshHost"]) ? device.Target : cfg["SshHost"]!;
        var port = cfg["SshPort"] ?? "2222";

        var bytes = await new IosAssetPuller(runner, host, port, key).PullAppBinaryAsync(device.Package, ct);
        if (bytes is null) return (false, null, "could not pull egginc binary off the device");

        await cache.PutAsync(CacheKind, platform, bytes, ct);
        logger.LogInformation("decomp: pulled egginc binary off {Id} ({Bytes} bytes)", device.Id, bytes.Length);
        return (true, bytes, null);
    }

    private static async Task<Device?> ResolveDeviceAsync(IDeviceStatusStore store, string? deviceId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(deviceId)) return await store.GetAsync(deviceId, ct);
        var devices = await store.EnabledDevicesAsync(ct);
        if (devices.Count == 0) return null;
        var latest = (await store.LatestPerDeviceAsync(ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.FirstOrDefault(d => latest.TryGetValue(d.Id, out var p) && p.Reachable);
        return reachable ?? devices[0];
    }
}
