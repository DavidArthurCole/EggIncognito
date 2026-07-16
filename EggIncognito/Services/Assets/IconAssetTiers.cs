using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Assets;

public sealed class IconDbTier(IServiceProvider services, ILogger<IconDbTier> logger) : IGameAssetTier
{
    public int Priority => 0;

    public bool Handles(GameAssetKey key) => key.Kind == "icon";

    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    public async Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct)
    {
        var db = Db;
        if (db is null) return null;
        try
        {
            var row = await db.StoredIcons.AsNoTracking()
                .Where(m => m.Name == key.Name)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(ct);
            return row is null ? null : ToAsset(key, row);
        }
        catch (Exception ex) { logger.LogWarning(ex, "icon db read failed {Name}", key.Name); return null; }
    }

    public async Task PutAsync(GameAsset asset, CancellationToken ct)
    {
        var db = Db;
        if (db is null) return;
        try
        {
            var existing = await db.StoredIcons.FirstOrDefaultAsync(m => m.Name == asset.Key.Name, ct);
            if (existing is null)
                db.StoredIcons.Add(new StoredIcon
                {
                    Name = asset.Key.Name,
                    ContentType = asset.ContentType,
                    Bytes = asset.Bytes,
                    ByteSize = asset.Bytes.Length,
                    Provenance = asset.Provenance
                });
            else
            {
                existing.Bytes = asset.Bytes;
                existing.ByteSize = asset.Bytes.Length;
                existing.ContentType = asset.ContentType;
                existing.Provenance = asset.Provenance;
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "icon db write failed {Name}", asset.Key.Name); }
    }

    private static GameAsset ToAsset(GameAssetKey key, StoredIcon row) =>
        new(key, row.Bytes, row.ContentType, $"db@{row.Name}",
            new DateTimeOffset(row.CreatedAt.UtcDateTime, TimeSpan.Zero));
}

public sealed class IconDiskTier(IconAssetCache cache) : IGameAssetTier
{
    public int Priority => 10;

    public bool Handles(GameAssetKey key) => key.Kind == "icon";

    public Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct)
    {
        var png = cache.TryGet(key.Name);
        var asset = png is null ? null
            : new GameAsset(key, png, "image/png", $"disk@{key.Name}", DateTimeOffset.UtcNow);
        return Task.FromResult(asset);
    }

    public Task PutAsync(GameAsset asset, CancellationToken ct) =>
        cache.PutAsync(asset.Key.Name, asset.Bytes, ct);
}
