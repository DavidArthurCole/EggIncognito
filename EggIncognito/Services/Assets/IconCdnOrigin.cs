using EggIncognito.Core.Services.Assets;

namespace EggIncognito.Services.Assets;

public sealed class IconCdnOrigin(IHttpClientFactory httpFactory, ILogger<IconCdnOrigin> logger) : IGameAssetOrigin
{
    private const string ArtifactsBase = "https://www.auxbrain.com/dlc/artifacts/1/";

    public bool Handles(GameAssetKey key) =>
        key.Kind == "icon"
        && (key.Name.StartsWith("afx_", StringComparison.Ordinal)
            || key.Name.StartsWith("egg_", StringComparison.Ordinal));

    public async Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct)
    {
        var url = ArtifactsBase + key.Name + ".png";
        try
        {
            var client = httpFactory.CreateClient("inspector");
            var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0) return null;
            logger.LogInformation("cdn icon: fetched {Name} ({Bytes}B)", key.Name, bytes.Length);
            return new GameAsset(key, bytes, "image/png", $"cdn@auxbrain:{key.Name}", DateTimeOffset.UtcNow);
        }
        catch (Exception ex) { logger.LogWarning(ex, "cdn icon fetch failed {Name}", key.Name); return null; }
    }
}
