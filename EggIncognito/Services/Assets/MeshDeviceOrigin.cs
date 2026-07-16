using EggIncognito.Core.Services.Assets;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Assets;

public sealed class MeshDeviceOrigin(DeviceAssetService devices, ILogger<MeshDeviceOrigin> logger) : IGameAssetOrigin
{
    public bool Handles(GameAssetKey key) => key.Kind == "mesh";

    public async Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct)
    {
        var read = await devices.ReadAsync(key.Platform, DeviceAssetKind.Mesh, key.Name, ct);
        if (!read.Ok || read.Bytes is null) return null;

        var decode = RpoMeshDecoder.Decode(read.Bytes, key.Name);
        if (!decode.Ok) return null;

        logger.LogInformation("device mesh: pulled {Stem} ({Plat})", key.Name, read.Platform);
        return new GameAsset(key with { Platform = read.Platform }, decode.Glb!, "model/gltf-binary",
            $"device@{read.Platform}:{key.Name}", DateTimeOffset.UtcNow);
    }
}
