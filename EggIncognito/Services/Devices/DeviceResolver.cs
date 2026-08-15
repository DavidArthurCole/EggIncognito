using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices;

public readonly record struct DeviceQuery(string? DeviceId = null, string? Platform = null);

public interface IDeviceResolver {
    Task<Device?> ResolveAsync(DeviceQuery query, CancellationToken ct);
}

public sealed class DeviceResolver(IServiceProvider services) : IDeviceResolver {
    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;
    private DeviceJobStore? Jobs => services.GetService(typeof(DeviceJobStore)) as DeviceJobStore;

    public async Task<Device?> ResolveAsync(DeviceQuery query, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;

        if (!string.IsNullOrEmpty(query.DeviceId) && await store.GetAsync(query.DeviceId, ct) is { } byId)
            return byId;

        var devices = await store.EnabledDevicesAsync(ct);
        if (!string.IsNullOrEmpty(query.Platform))
            devices = [.. devices.Where(d => Platforms.Matches(d.Platform, query.Platform))];
        if (devices.Count == 0) return null;

        if (Jobs is not { } jobs) return devices[0];
        var latest = (await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.FirstOrDefault(d => latest.TryGetValue(d.Id, out var p) && p.Reachable == true);
        return reachable ?? devices[0];
    }
}
