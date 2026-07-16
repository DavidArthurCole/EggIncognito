using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Assets;

public sealed class MeshDbTier(IServiceProvider services, ILogger<MeshDbTier> logger) : IGameAssetTier
{
    public int Priority => 0;

    public bool Handles(GameAssetKey key) => key.Kind == "mesh";

    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    public async Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct)
    {
        var db = Db;
        if (db is null) return null;
        try
        {
            var q = db.StoredMeshes.AsNoTracking().Where(m => m.Stem == key.Name);
            if (key.Platform is not null) q = q.Where(m => m.Platform == key.Platform);
            var row = await q.OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync(ct);
            return row is null ? null : ToAsset(key, row);
        }
        catch (Exception ex) { logger.LogWarning(ex, "mesh db read failed {Stem}", key.Name); return null; }
    }

    public async Task PutAsync(GameAsset asset, CancellationToken ct)
    {
        var db = Db;
        if (db is null) return;
        var platform = asset.Key.Platform ?? "db";
        try
        {
            var existing = await db.StoredMeshes.FirstOrDefaultAsync(m => m.Platform == platform && m.Stem == asset.Key.Name, ct);
            if (existing is null)
                db.StoredMeshes.Add(new StoredMesh { Platform = platform, Stem = asset.Key.Name, Glb = asset.Bytes, ByteSize = asset.Bytes.Length });
            else { existing.Glb = asset.Bytes; existing.ByteSize = asset.Bytes.Length; }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { logger.LogWarning(ex, "mesh db write failed {Stem}", asset.Key.Name); }
    }

    private static GameAsset ToAsset(GameAssetKey key, StoredMesh row) =>
        new(key with { Platform = row.Platform }, row.Glb, "model/gltf-binary",
            $"db@{row.Platform}:{row.Stem}", new DateTimeOffset(row.CreatedAt.UtcDateTime, TimeSpan.Zero));
}

public sealed class MeshDiskTier(MeshAssetCache cache) : IGameAssetTier
{
    public int Priority => 10;

    public bool Handles(GameAssetKey key) => key.Kind == "mesh" && key.Platform is not null;

    public Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct)
    {
        var glb = cache.TryGet(key.Platform!, key.Name);
        var asset = glb is null ? null
            : new GameAsset(key, glb, "model/gltf-binary", $"disk@{key.Platform}:{key.Name}", DateTimeOffset.UtcNow);
        return Task.FromResult(asset);
    }

    public Task PutAsync(GameAsset asset, CancellationToken ct) =>
        cache.PutAsync(asset.Key.Platform ?? "db", asset.Key.Name, asset.Bytes, ct);
}
