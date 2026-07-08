using System.Text.Json;

namespace EggIncognito.Services;

// Locally hosts the game's *Config (ei/get_config ConfigResponse) per platform, so EGI has an offline copy
// without hitting the live API every time. Stored as JSON under <ConfigStore:Dir>/<platform>.json. Disabled
// (reads null, writes no-op) when no dir is configured.
public sealed class GameConfigStore(IConfiguration config)
{
    // Dir precedence: explicit ConfigStore:Dir, else <ShipAssets:OutputDir>/config. Null disables the store.
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

    public bool Enabled => Dir is not null;

    public sealed record StoredConfig(string Platform, string Json, DateTimeOffset SavedAt, long Bytes);

    public async Task SaveAsync(string platform, string json, CancellationToken ct)
    {
        var dir = Dir;
        if (dir is null) return;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{Safe(platform)}.json"), json, ct);
    }

    public StoredConfig? Get(string platform)
    {
        var dir = Dir;
        if (dir is null) return null;
        var path = Path.Combine(dir, $"{Safe(platform)}.json");
        if (!File.Exists(path)) return null;
        try
        {
            var info = new FileInfo(path);
            return new StoredConfig(platform, File.ReadAllText(path),
                new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length);
        }
        catch { return null; }
    }

    // Platforms with a stored config + their save time + size, for the admin overview.
    public IReadOnlyList<(string Platform, DateTimeOffset SavedAt, long Bytes)> List()
    {
        var dir = Dir;
        if (dir is null || !Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(p => new FileInfo(p))
            .Select(f => (Platform: Path.GetFileNameWithoutExtension(f.Name),
                          SavedAt: new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), Bytes: f.Length))
            .OrderBy(x => x.Platform, StringComparer.Ordinal)
            .ToList();
    }

    private static string Safe(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var ch in s) if (char.IsLetterOrDigit(ch) || ch is '_' or '-') buf[n++] = ch;
        return n == 0 ? "unknown" : new string(buf[..n]);
    }
}
