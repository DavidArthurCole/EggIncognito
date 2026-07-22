namespace EggIncognito.Services.Assets;

public sealed class IconAssetCache(IConfiguration config) {
    private string? CacheRoot {
        get {
            var dir = config["ShipAssets:OutputDir"];
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "icons");
        }
    }

    public bool Enabled => CacheRoot is not null;

    public byte[]? TryGet(string name) {
        var path = PathFor(name);
        if (path is null || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); } catch { return null; }
    }

    public async Task PutAsync(string name, byte[] bytes, CancellationToken ct) {
        var path = PathFor(name);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    private string? PathFor(string name) {
        var root = CacheRoot;
        return root is null || string.IsNullOrEmpty(name) ? null : Path.Combine(root, Safe(name) + ".png");
    }

    private static string Safe(string s) {
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-') buf[n++] = ch;
        return n == 0 ? "_" : new string(buf[..n]);
    }
}
