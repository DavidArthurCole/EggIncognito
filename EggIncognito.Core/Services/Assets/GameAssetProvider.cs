namespace EggIncognito.Core.Services.Assets;

public readonly record struct GameAssetKey(string Kind, string? Platform, string Name, string? Version = null);

public sealed record GameAsset(GameAssetKey Key, byte[] Bytes, string ContentType, string Provenance, DateTimeOffset PulledAt);

public interface IGameAssetTier {
    int Priority { get; }
    bool Handles(GameAssetKey key);
    Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct);
    Task PutAsync(GameAsset asset, CancellationToken ct);
}

public interface IGameAssetOrigin {
    bool Handles(GameAssetKey key);
    Task<GameAsset?> FetchAsync(GameAssetKey key, CancellationToken ct);
}

public sealed record GameAssetResult(bool Ok, GameAsset? Asset, string Source, string? Diagnostics);

public sealed class GameAssetProvider(IEnumerable<IGameAssetTier> tiers, IEnumerable<IGameAssetOrigin> origins) {
    private readonly IReadOnlyList<IGameAssetTier> _tiers = tiers.OrderBy(t => t.Priority).ToList();
    private readonly IReadOnlyList<IGameAssetOrigin> _origins = origins.ToList();

    public async Task<GameAssetResult> GetAsync(GameAssetKey key, CancellationToken ct) {
        var applicable = _tiers.Where(t => t.Handles(key)).ToList();

        for (var i = 0; i < applicable.Count; i++) {
            var hit = await applicable[i].TryGetAsync(key, ct);
            if (hit is null) continue;
            for (var j = 0; j < i; j++)
                await SafePutAsync(applicable[j], hit, ct);
            return new GameAssetResult(true, hit, TierName(applicable[i]), null);
        }

        var origin = _origins.FirstOrDefault(o => o.Handles(key));
        if (origin is null)
            return new GameAssetResult(false, null, "none", "no cached asset and no origin for this key");

        GameAsset? fetched;
        try { fetched = await origin.FetchAsync(key, ct); } catch (Exception ex) { return new GameAssetResult(false, null, "origin", ex.Message); }
        if (fetched is null)
            return new GameAssetResult(false, null, "origin", "origin returned no asset");

        foreach (var tier in applicable)
            await SafePutAsync(tier, fetched, ct);
        return new GameAssetResult(true, fetched, "origin", null);
    }

    private static async Task SafePutAsync(IGameAssetTier tier, GameAsset asset, CancellationToken ct) {
        try { await tier.PutAsync(asset, ct); } catch { }
    }

    private static string TierName(IGameAssetTier tier) => tier.GetType().Name;
}
