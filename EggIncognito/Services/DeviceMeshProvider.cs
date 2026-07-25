using EggIncognito.Core.Services.Assets;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

public sealed class DeviceMeshProvider(
    IServiceProvider services,
    IProcessRunner runner,
    IDeviceConnectionFactory connections,
    GameAssetProvider assets) {
    private const string PlatformAndroid = "android";
    private const string PlatformIos = "ios";

    private IDeviceStatusStore? Store => services.GetService(typeof(IDeviceStatusStore)) as IDeviceStatusStore;

    private static Result Err(int status, string diag) => new(false, null, diag, status);


    public async Task<Result> GetGlbAsync(string stem, string? deviceId, CancellationToken ct) {
        if (string.IsNullOrEmpty(stem) || stem.IndexOfAny(['/', '\\', '.']) >= 0)
            return Err(400, "invalid mesh name");

        string? platform = (await ResolveDeviceAsync(deviceId, ct))?.Platform;
        var result = await assets.GetAsync(new GameAssetKey("mesh", platform, stem), ct);
        return result.Ok
            ? new Result(true, result.Asset!.Bytes, null, 200)
            : Err(503, result.Diagnostics ?? "mesh not cached and no asset-source device available");
    }


    public async Task<(bool Ok, RpoMeshDecoder.DecodeResult? Stats, string? Diagnostics)> GetDecodeStatsAsync(
        string stem, string? deviceId, CancellationToken ct) {
        if (string.IsNullOrEmpty(stem) || stem.IndexOfAny(['/', '\\', '.']) >= 0)
            return (false, null, "invalid mesh name");
        var device = await ResolveDeviceAsync(deviceId, ct);
        if (device is null) return (false, null, "no asset-source device available");
        (byte[]? rpo, _) = await PullRpoAsync(device, stem, ct);
        if (rpo is null) return (false, null, "mesh not found on device");
        return (true, RpoMeshDecoder.Decode(rpo, stem), null);
    }

    private async Task<(byte[]? Rpo, Result? Err)> PullRpoAsync(Device device, string stem, CancellationToken ct) {
        byte[]? rpo;
        if (device.Platform == PlatformIos) {
            if (connections.Ios(device.Target) is not { } conn)
                return (null, Err(503, "ios mesh pull needs DeviceUpdate:Ios:SshKeyPath configured"));
            rpo = await new IosAssetPuller(conn).PullOneRpoAsync(device.Package, stem, ct);
        } else if (device.Platform == PlatformAndroid) {
            byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
            if (apk is null) return (null, Err(502, "could not pull base.apk from the device"));
            rpo = RpoAssetLister.ReadStem(apk, stem);
        } else {
            return (null, Err(501, $"no mesh pull for platform {device.Platform}"));
        }

        if (rpo is null) return (null, Err(404, "mesh not found on device"));
        return (rpo, null);
    }


    public async Task<(bool Ok, IReadOnlyList<string> Stems, string? Diagnostics)> ListStemsAsync(string? deviceId,
        CancellationToken ct) {
        var device = await ResolveDeviceAsync(deviceId, ct);
        if (device is null) return (false, [], "no asset-source device available");
        if (device.Platform == PlatformIos) {
            if (connections.Ios(device.Target) is not { } conn)
                return (false, [], "ios mesh listing needs DeviceUpdate:Ios:SshKeyPath configured");
            var names = await new IosAssetPuller(conn).ListRposAsync(device.Package, ct);
            return names.Count > 0 ? (true, names, null) : (false, [], "no meshes found on the device bundle");
        }

        if (device.Platform != PlatformAndroid)
            return (false, [], $"no mesh listing for platform {device.Platform}");
        byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
        if (apk is null) return (false, [], "could not pull base.apk from the device");
        return (true, RpoAssetLister.ListStems(apk), null);
    }


    public async Task<(bool Ok, IReadOnlyList<string> Stems, IReadOnlyDictionary<string, RpoMeshDecoder.DecodeResult>
            Stats, string? Diagnostics)>
        ListStemsWithStatsAsync(string? deviceId, Func<IReadOnlyList<string>, IEnumerable<string>> selectStatsFor,
            CancellationToken ct) {
        var empty =
            (IReadOnlyDictionary<string, RpoMeshDecoder.DecodeResult>)
            new Dictionary<string, RpoMeshDecoder.DecodeResult>();
        var device = await ResolveDeviceAsync(deviceId, ct);
        if (device is null) return (false, [], empty, "no asset-source device available");

        (var rpos, string? diag) = device.Platform switch {
            PlatformAndroid => await PullAndroidRposAsync(device, ct),
            PlatformIos => await PullIosRposAsync(device, ct),
            _ => (null, $"no mesh listing for platform {device.Platform}")
        };
        if (rpos is null) return (false, [], empty, diag);

        var stems = rpos.Keys.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var stats = new Dictionary<string, RpoMeshDecoder.DecodeResult>(StringComparer.Ordinal);
        foreach (string stem in selectStatsFor(stems).Distinct(StringComparer.Ordinal)) {
            if (rpos.TryGetValue(stem, out byte[]? rpo))
                stats[stem] = RpoMeshDecoder.Decode(rpo, stem);
        }

        return (true, stems, stats, null);
    }

    private async Task<(IReadOnlyDictionary<string, byte[]>?, string?)> PullAndroidRposAsync(Device device,
        CancellationToken ct) {
        byte[]? apk = await new DeviceApkPuller(runner).PullBaseSplitAsync(device.Target, device.Package, ct);
        if (apk is null) return (null, "could not pull base.apk from the device");
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string stem in RpoAssetLister.ListStems(apk)) {
            if (RpoAssetLister.ReadStem(apk, stem) is { } b)
                map[stem] = b;
        }

        return (map, null);
    }


    private async Task<(IReadOnlyDictionary<string, byte[]>?, string?)> PullIosRposAsync(Device device,
        CancellationToken ct) {
        if (connections.Ios(device.Target) is not { } conn)
            return (null, "ios mesh listing needs DeviceUpdate:Ios:SshKeyPath configured");
        byte[]? tar = await new IosAssetPuller(conn).PullRposTarAsync(device.Package, ct);
        if (tar is null) return (null, "could not pull the rpos tarball off the device");
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string name, byte[] bytes) in TarReader.Read(tar)) {
            if (bytes.Length == 0) continue;
            if (!name.EndsWith(".rpo", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".rpoz", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            map[StemOf(name)] = bytes;
        }

        return (map, null);
    }

    private static string StemOf(string path) {
        int slash = path.LastIndexOfAny(['/', '\\']);
        string name = slash >= 0 ? path[(slash + 1)..] : path;
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }


    private async Task<Device?> ResolveDeviceAsync(string? deviceId, CancellationToken ct) {
        var store = Store;
        if (store is null) return null;
        if (!string.IsNullOrEmpty(deviceId)) return await store.GetAsync(deviceId, ct);

        var devices = await store.EnabledDevicesAsync(ct);
        if (devices.Count == 0) return null;
        var latest = (await store.LatestPerDeviceAsync(ct)).ToDictionary(p => p.DeviceId);
        var reachable = devices.FirstOrDefault(d => latest.TryGetValue(d.Id, out var p) && p.Reachable);
        return reachable ?? devices[0];
    }

    public sealed record Result(bool Ok, byte[]? Glb, string? Diagnostics, int Status);
}
