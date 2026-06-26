namespace EggIncognito.Services;

// On-disk cache of decoded mesh .glb files, so the playground + API serve a pre-computed glb instead of
// re-pulling the archive off a device and re-decoding on every request. Keyed by (platform, stem). Lives
// under ShipAssets:OutputDir/cache when configured; a null/empty config disables the cache (every read is a
// miss, every write a no-op) so nothing breaks without an output dir.
public sealed class MeshAssetCache(IConfiguration config)
{
    private string? CacheRoot
    {
        get
        {
            var dir = config["ShipAssets:OutputDir"];
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "cache");
        }
    }

    // The cached glb bytes for (platform, stem), or null on a miss / no cache configured. Decoded glb only
    // (un-animated); animation is applied on top per request so one cache entry serves every animation kind.
    public byte[]? TryGet(string platform, string stem)
    {
        var path = PathFor(platform, stem);
        if (path is null || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    public async Task PutAsync(string platform, string stem, byte[] glb, CancellationToken ct)
    {
        var path = PathFor(platform, stem);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, glb, ct);
    }

    // Count of cached glbs for a platform, for admin reporting.
    public int Count(string platform)
    {
        var root = CacheRoot;
        if (root is null) return 0;
        var dir = Path.Combine(root, Safe(platform));
        return Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.glb").Count() : 0;
    }

    public bool Enabled => CacheRoot is not null;

    // <cacheRoot>/<platform>/<stem>.glb. Stem + platform are sanitized to a safe filename (no traversal).
    private string? PathFor(string platform, string stem)
    {
        var root = CacheRoot;
        if (root is null || string.IsNullOrEmpty(stem)) return null;
        return Path.Combine(root, Safe(platform), Safe(stem) + ".glb");
    }

    // Strips anything but the filename-safe set so a crafted stem cannot escape the cache dir.
    private static string Safe(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-') buf[n++] = ch;
        return n == 0 ? "_" : new string(buf[..n]);
    }
}
