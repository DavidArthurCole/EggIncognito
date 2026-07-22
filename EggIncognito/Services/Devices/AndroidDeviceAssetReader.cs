using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Devices;

public sealed class AndroidDeviceAssetReader(IProcessRunner runner) : IDeviceAssetReader {
    public string Platform => "android";

    public async Task<byte[]?> ReadAsync(Device device, DeviceAssetKind kind, string name, CancellationToken ct) {
        var apk = await PullApkAsync(device, ct);
        return apk is null
            ? null
            : kind switch {
                DeviceAssetKind.Mesh => RpoAssetLister.ReadStem(apk, name),
                DeviceAssetKind.Texture => ApkTextureLister.ReadStem(apk, name),
                _ => null
            };
    }

    public async Task<IReadOnlyList<string>> ListAsync(Device device, DeviceAssetKind kind, CancellationToken ct) {
        var apk = await PullApkAsync(device, ct);
        return apk is null
            ? []
            : kind switch {
                DeviceAssetKind.Mesh => RpoAssetLister.ListStems(apk),
                DeviceAssetKind.Texture => ApkTextureLister.ListStems(apk),
                _ => []
            };
    }

    private Task<byte[]?> PullApkAsync(Device device, CancellationToken ct) =>
        new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
}
