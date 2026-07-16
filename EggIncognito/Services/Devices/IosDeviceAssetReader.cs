using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

public sealed class IosDeviceAssetReader(IProcessRunner runner, IConfiguration config) : IDeviceAssetReader
{
    public string Platform => "ios";

    public Task<byte[]?> ReadAsync(Device device, DeviceAssetKind kind, string name, CancellationToken ct)
    {
        if (Puller(device) is not { } p) return Task.FromResult<byte[]?>(null);
        return kind switch
        {
            DeviceAssetKind.Mesh => p.PullOneRpoAsync(device.Package, name, ct),
            DeviceAssetKind.Texture => p.PullOneTextureAsync(device.Package, name, ct),
            _ => Task.FromResult<byte[]?>(null)
        };
    }

    public async Task<IReadOnlyList<string>> ListAsync(Device device, DeviceAssetKind kind, CancellationToken ct)
    {
        if (Puller(device) is not { } p) return [];
        return kind switch
        {
            DeviceAssetKind.Mesh => await p.ListRposAsync(device.Package, ct),
            DeviceAssetKind.Texture => await p.ListTexturesAsync(device.Package, ct),
            _ => []
        };
    }

    private IosAssetPuller? Puller(Device device)
    {
        var cfg = config.GetSection("DeviceUpdate").GetSection("Ios");
        var key = cfg["SshKeyPath"];
        if (string.IsNullOrEmpty(key)) return null;
        var host = string.IsNullOrEmpty(cfg["SshHost"]) ? device.Target : cfg["SshHost"]!;
        return new IosAssetPuller(runner, host, cfg["SshPort"] ?? "2222", key);
    }
}
