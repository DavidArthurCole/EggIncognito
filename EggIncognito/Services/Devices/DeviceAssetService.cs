using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceAssetService(IDevicePlatforms platforms, IDeviceResolver resolver) {
    public async Task<Read> ReadAsync(string? platform, DeviceAssetKind kind, string name, CancellationToken ct) {
        var device = await resolver.ResolveAsync(new DeviceQuery(Platform: platform), ct);
        if (device is null) return new Read(false, null, null, "no asset-source device available");
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);
        var result = await platforms.For(device.Platform).ReadAssetAsync(target, kind, name, ct);
        return result is { Ok: true, Value: { } bytes }
            ? new Read(true, bytes, device.Platform, null)
            : new Read(false, null, device.Platform, result.Note ?? "asset not found on device");
    }

    public async Task<(IReadOnlyList<string> Names, string? Platform, string? Diagnostics)> ListAsync(
        string? platform, DeviceAssetKind kind, CancellationToken ct) {
        var device = await resolver.ResolveAsync(new DeviceQuery(Platform: platform), ct);
        if (device is null) return ([], null, "no asset-source device available");
        var target = new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);
        var result = await platforms.For(device.Platform).ListAssetsAsync(target, kind, ct);
        return result is { Ok: true, Value: { } names }
            ? (names, device.Platform, null)
            : ([], device.Platform, result.Note);
    }

    public readonly record struct Read(bool Ok, byte[]? Bytes, string? Platform, string? Diagnostics);
}
