using System.Globalization;
using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

public sealed class SymbolizedBinaryStore(string ipaDir, Func<byte[], bool>? isSymbolized = null) {
    private readonly Func<byte[], bool> _isSymbolized = isSymbolized ?? (b => MachoSymbols.Read(b).Count > 50_000);

    public IReadOnlyList<string> ListVersions()
        => BuildIndex().Keys.OrderByDescending(VersionKey).ToList();

    public Result Get(string? version) {
        var index = BuildIndex();
        if (index.Count == 0) {
            return new Result(false, null, "", false,
                $"no symbolized binary available; add a symbolized .ipa to {ipaDir}");
        }

        if (!string.IsNullOrEmpty(version) && index.TryGetValue(version, out byte[]? exact))
            return new Result(true, exact, version, true, "ok");

        string newest = index.Keys.OrderByDescending(VersionKey).First();
        string note = string.IsNullOrEmpty(version)
            ? "no version requested; using newest symbolized build"
            : $"no symbolized build for {version}; using newest ({newest})";
        return new Result(true, index[newest], newest, false, note);
    }

    private Dictionary<string, byte[]> BuildIndex() {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!Directory.Exists(ipaDir)) return map;
        foreach (string path in Directory.EnumerateFiles(ipaDir, "*.ipa")) {
            try {
                using var zip = ZipFile.OpenRead(path);
                (string? version, byte[]? exec) = SymbolizedIpa.Read(zip);
                if (version is null || exec is null) continue;
                if (!_isSymbolized(exec)) continue;
                map.TryAdd(version, exec);
            } catch {
                /* skip malformed ipa */
            }
        }

        return map;
    }


    private static (int, int, int, int) VersionKey(string v) {
        string[] p = v.Split('.');

        int N(int i) {
            return i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                ? n
                : 0;
        }

        return (N(0), N(1), N(2), N(3));
    }

    public readonly record struct Result(bool Ok, byte[]? Bytes, string Version, bool ExactVersion, string Diagnostics);
}
