namespace EggIncognito.Services;

public sealed class MeshAssetCache(IConfiguration config) {
    private string? CacheRoot {
        get {
            string? dir = config["ShipAssets:OutputDir"];
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "cache");
        }
    }

    public bool Enabled => CacheRoot is not null;

    public byte[]? TryGet(string platform, string stem) {
        string? path = PathFor(platform, stem);
        if (path is null || !File.Exists(path)) return null;
        try {
            return File.ReadAllBytes(path);
        } catch {
            return null;
        }
    }

    public async Task PutAsync(string platform, string stem, byte[] glb, CancellationToken ct) {
        string? path = PathFor(platform, stem);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, glb, ct);
    }

    public IReadOnlyList<CachedMesh> List(string platform) {
        string? root = CacheRoot;
        if (root is null) return [];
        string dir = Path.Combine(root, Safe(platform));
        return !Directory.Exists(dir)
            ? []
            : [
                .. Directory.EnumerateFiles(dir, "*.glb")
                    .Select(p => new FileInfo(p))
                    .Select(f => new CachedMesh(Path.GetFileNameWithoutExtension(f.Name), f.Length,
                        new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
                    .OrderBy(m => m.Stem, StringComparer.Ordinal)
            ];
    }

    public bool Delete(string platform, string stem) {
        string? path = PathFor(platform, stem);
        if (path is null || !File.Exists(path)) return false;
        try {
            File.Delete(path);
            return true;
        } catch {
            return false;
        }
    }

    public int Clear(string platform) {
        string? root = CacheRoot;
        if (root is null) return 0;
        string dir = Path.Combine(root, Safe(platform));
        if (!Directory.Exists(dir)) return 0;
        int n = 0;
        foreach (string f in Directory.EnumerateFiles(dir, "*.glb")) {
            try {
                File.Delete(f);
                n++;
            } catch {
                /* skip locked */
            }
        }

        return n;
    }

    private string? PathFor(string platform, string stem) {
        string? root = CacheRoot;
        return root is null || string.IsNullOrEmpty(stem)
            ? null
            : Path.Combine(root, Safe(platform), Safe(stem) + ".glb");
    }

    private static string Safe(string s) {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char ch in s) {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
                buf[n++] = ch;
        }

        return n == 0 ? "_" : new string(buf[..n]);
    }

    public sealed record CachedMesh(string Stem, long Bytes, DateTimeOffset CachedAt);
}
