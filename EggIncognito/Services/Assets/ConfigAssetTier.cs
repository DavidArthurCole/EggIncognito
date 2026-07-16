using EggIncognito.Core.Services.Assets;

namespace EggIncognito.Services.Assets;

public sealed class ConfigDiskTier(IConfiguration config) : IGameAssetTier
{
    public int Priority => 10;

    public bool Handles(GameAssetKey key) => key.Kind == "config";

    private string? Dir
    {
        get
        {
            var explicitDir = config["ConfigStore:Dir"];
            if (!string.IsNullOrEmpty(explicitDir)) return explicitDir;
            var assets = config["ShipAssets:OutputDir"];
            return string.IsNullOrEmpty(assets) ? null : Path.Combine(assets, "config");
        }
    }

    public Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (path is null || !File.Exists(path)) return Task.FromResult<GameAsset?>(null);
        try
        {
            var info = new FileInfo(path);
            var bytes = File.ReadAllBytes(path);
            var asset = new GameAsset(key, bytes, "application/json",
                $"disk@{key.Platform}:{key.Name}", new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            return Task.FromResult<GameAsset?>(asset);
        }
        catch { return Task.FromResult<GameAsset?>(null); }
    }

    public async Task PutAsync(GameAsset asset, CancellationToken ct)
    {
        var path = PathFor(asset.Key);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, asset.Bytes, ct);
    }

    private string? PathFor(GameAssetKey key)
    {
        var dir = Dir;
        if (dir is null) return null;
        return Path.Combine(dir, $"{Safe(key.Name)}_{Safe(key.Platform ?? "any")}.json");
    }

    private static string Safe(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s) if (char.IsLetterOrDigit(ch) || ch is '_' or '-') buf[n++] = ch;
        return n == 0 ? "unknown" : new string(buf[..n]);
    }
}
