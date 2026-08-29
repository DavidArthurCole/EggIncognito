using EggIncognito.Core.Services.Assets;
namespace EggIncognito.Services.Assets;

public sealed class ConfigDiskTier(IConfiguration config) : IGameAssetTier {
    private string? Dir {
        get {
            string? explicitDir = config["ConfigStore:Dir"];
            if (!string.IsNullOrEmpty(explicitDir)) return explicitDir;
            string? assets = config["ShipAssets:OutputDir"];
            return string.IsNullOrEmpty(assets) ? null : Path.Combine(assets, "config");
        }
    }

    public int Priority => 10;

    public bool CanHandle(GameAssetKey key) => key.Kind == "config";

    public Task<GameAsset?> TryGetAsync(GameAssetKey key, CancellationToken ct) {
        string? path = PathFor(key);
        if (path is null || !File.Exists(path)) return Task.FromResult<GameAsset?>(null);
        try {
            var info = new FileInfo(path);
            byte[] bytes = File.ReadAllBytes(path);
            var asset = new GameAsset(key, bytes, "application/json",
                $"disk@{key.Platform}:{key.Name}", new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            return Task.FromResult<GameAsset?>(asset);
        } catch {
            return Task.FromResult<GameAsset?>(null);
        }
    }

    public async Task PutAsync(GameAsset asset, CancellationToken ct) {
        string? path = PathFor(asset.Key);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, asset.Bytes, ct);
    }

    private string? PathFor(GameAssetKey key) {
        string? dir = Dir;
        return dir is null ? null : Path.Combine(dir, $"{Safe(key.Name)}_{Safe(key.Platform ?? "any")}.json");
    }

    private static string Safe(string s) {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char ch in s) {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
                buf[n++] = ch;
        }

        return n == 0 ? "unknown" : new string(buf[..n]);
    }
}
