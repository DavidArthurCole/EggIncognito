using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Assets;

public sealed class MeshDeviceOrigin(
    IServiceProvider services, IProcessRunner runner, IConfiguration config, ILogger<MeshDeviceOrigin> logger) : IGameAssetOrigin
{
    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;

    public bool Handles(GameAssetKey key) => key.Kind == "mesh";

    public async Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct)
    {
        var device = await ResolveDeviceAsync(key.Platform, ct);
        if (device is null) return null;

        var rpo = await PullRpoAsync(device, key.Name, ct);
        if (rpo is null) return null;

        var decode = RpoMeshDecoder.Decode(rpo, key.Name);
        if (!decode.Ok) return null;

        logger.LogInformation("device mesh: pulled {Stem} off {Id} ({Plat})", key.Name, device.Id, device.Platform);
        return new GameAsset(key with { Platform = device.Platform }, decode.Glb!, "model/gltf-binary",
            $"device@{device.Platform}:{key.Name}", DateTimeOffset.UtcNow);
    }

    private async Task<byte[]?> PullRpoAsync(Device device, string stem, CancellationToken ct)
    {
        if (device.Platform == PlatformIos)
        {
            if (IosSsh(device) is not { } ssh) return null;
            return await new IosAssetPuller(runner, ssh.Host, ssh.Port, ssh.Key).PullOneRpoAsync(device.Package, stem, ct);
        }
        if (device.Platform == PlatformAndroid)
        {
            var apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            return apk is null ? null : RpoAssetLister.ReadStem(apk, stem);
        }
        return null;
    }

    private async Task<Device?> ResolveDeviceAsync(string? platform, CancellationToken ct)
    {
        var store = Store;
        if (store is null) return null;
        var devices = await store.EnabledDevicesAsync(ct);
        if (platform is not null) devices = devices.Where(d => d.Platform == platform).ToList();
        if (devices.Count == 0) return null;
        var latest = (await store.LatestPerDeviceAsync(ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.FirstOrDefault(d => latest.TryGetValue(d.Id, out var p) && p.Reachable);
        return reachable ?? devices[0];
    }

    private (string Host, string Port, string Key)? IosSsh(Device device)
    {
        var cfg = config.GetSection("DeviceUpdate").GetSection("Ios");
        var key = cfg["SshKeyPath"];
        if (string.IsNullOrEmpty(key)) return null;
        var host = string.IsNullOrEmpty(cfg["SshHost"]) ? device.Target : cfg["SshHost"]!;
        return (host, cfg["SshPort"] ?? "2222", key);
    }
}
