using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public sealed class DeviceAssetService(IServiceProvider services, IEnumerable<IDeviceAssetReader> readers) {
    public readonly record struct Read(bool Ok, byte[]? Bytes, string? Platform, string? Diagnostics);

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;

    public async Task<Read> ReadAsync(string? platform, DeviceAssetKind kind, string name, CancellationToken ct) {
        var device = await ResolveDeviceAsync(platform, ct);
        if (device is null) return new Read(false, null, null, "no asset-source device available");
        var reader = readers.FirstOrDefault(r => r.Platform == device.Platform);
        if (reader is null) return new Read(false, null, device.Platform, $"no asset reader for platform {device.Platform}");
        var bytes = await reader.ReadAsync(device, kind, name, ct);
        return bytes is null
            ? new Read(false, null, device.Platform, "asset not found on device")
            : new Read(true, bytes, device.Platform, null);
    }

    public async Task<(IReadOnlyList<string> Names, string? Platform, string? Diagnostics)> ListAsync(
        string? platform, DeviceAssetKind kind, CancellationToken ct) {
        var device = await ResolveDeviceAsync(platform, ct);
        if (device is null) return ([], null, "no asset-source device available");
        var reader = readers.FirstOrDefault(r => r.Platform == device.Platform);
        if (reader is null) return ([], device.Platform, $"no asset reader for platform {device.Platform}");
        return (await reader.ListAsync(device, kind, ct), device.Platform, null);
    }

    public async Task<Device?> ResolveDeviceAsync(string? platform, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;
        var devices = await store.EnabledDevicesAsync(ct);
        if (platform is not null) devices = [.. devices.Where(d => d.Platform == platform)];
        if (devices.Count == 0) return null;
        var latest = (await store.LatestPerDeviceAsync(ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.FirstOrDefault(d => latest.TryGetValue(d.Id, out var p) && p.Reachable);
        return reachable ?? devices[0];
    }
}
