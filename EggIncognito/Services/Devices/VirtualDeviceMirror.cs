using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

public static class VirtualDeviceMirror {
    private const string Package = "com.auxbrain.egginc";

    public static async Task<IReadOnlyList<ProvisionedInstance>> RemoteLiveInstancesAsync(
        IDeviceProvisioners provisioners, VirtualDeviceConfig config, CancellationToken ct) {
        if (!RemoteDeviceProvisioner.IsRemoteKind(config.Kind)) return [];

        var listed = await provisioners.For(config.Kind).ListAsync(ct);
        if (!listed.Ok || listed.Value is not { } instances) return [];

        return [.. instances
            .Where(i => i.DeviceId is { Length: > 0 } && i.AdbSerial is { Length: > 0 }
                        && ProvisionStates.IsLive(i.State))];
    }

    public static async Task<IReadOnlyList<Device>> RemoteLiveDevicesAsync(
        IDeviceProvisioners provisioners, VirtualDeviceConfig config, CancellationToken ct) =>
        [.. (await RemoteLiveInstancesAsync(provisioners, config, ct)).Select(ToDevice)];

    public static Device ToDevice(ProvisionedInstance instance) => new() {
        Id = instance.DeviceId!,
        Platform = Platforms.Android,
        Label = instance.DeviceId!,
        Target = instance.AdbSerial!,
        Package = Package,
        Enabled = true,
        Origin = DeviceOrigins.Virtual
    };

    public static async Task<DeviceTarget?> ResolveTargetAsync(
        IDeviceProvisioners provisioners, VirtualDeviceConfig config, string deviceId, CancellationToken ct) {
        var devices = await RemoteLiveDevicesAsync(provisioners, config, ct);
        var device = devices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal));
        return device is null ? null : new DeviceTarget(device.Id, device.Platform, device.Target, device.Package);
    }
}
