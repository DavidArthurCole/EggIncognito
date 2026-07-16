using EggIncognito.Core.Services.Assets;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services.Assets;

public sealed class IconDeviceOrigin(DeviceAssetService devices, ILogger<IconDeviceOrigin> logger) : IGameAssetOrigin
{
    public bool Handles(GameAssetKey key) => key.Kind == "icon";

    public async Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct)
    {
        var read = await devices.ReadAsync(key.Platform, DeviceAssetKind.Texture, key.Name, ct);
        if (!read.Ok || read.Bytes is null) return null;

        logger.LogInformation("device icon: pulled {Name} ({Plat})", key.Name, read.Platform);
        return new GameAsset(key with { Platform = read.Platform }, read.Bytes, "image/png",
            $"device@{read.Platform}:{key.Name}", DateTimeOffset.UtcNow);
    }
}
