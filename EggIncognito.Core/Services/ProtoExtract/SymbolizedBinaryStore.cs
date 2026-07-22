using System.Globalization;
using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

public sealed class SymbolizedBinaryStore(string ipaDir, Func<byte[], bool>? isSymbolized = null) {
    public readonly record struct Result(bool Ok, byte[]? Bytes, string Version, bool ExactVersion, string Diagnostics);

    private readonly string _ipaDir = ipaDir;
    private readonly Func<byte[], bool> _isSymbolized = isSymbolized ?? (b => MachoSymbols.Read(b).Count > 50_000);

    public IReadOnlyList<string> ListVersions()
        => BuildIndex().Keys.OrderByDescending(VersionKey).ToList();

    public Result Get(string? version) {
        var index = BuildIndex();
        if (index.Count == 0)
            return new Result(false, null, "", false, $"no symbolized binary available; add a symbolized .ipa to {_ipaDir}");

        if (!string.IsNullOrEmpty(version) && index.TryGetValue(version, out var exact))
            return new Result(true, exact, version, true, "ok");

        var newest = index.Keys.OrderByDescending(VersionKey).First();
        var note = string.IsNullOrEmpty(version)
            ? "no version requested; using newest symbolized build"
            : $"no symbolized build for {version}; using newest ({newest})";
        return new Result(true, index[newest], newest, false, note);
    }

    private Dictionary<string, byte[]> BuildIndex() {
        var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (!Directory.Exists(_ipaDir)) return map;
        foreach (var path in Directory.EnumerateFiles(_ipaDir, "*.ipa")) {
            try {
                using var zip = ZipFile.OpenRead(path);
                var (version, exec) = ReadIpa(zip);
                if (version is null || exec is null) continue;
                if (!_isSymbolized(exec)) continue;
                map.TryAdd(version, exec);
            } catch { /* skip malformed ipa */ }
        }
        return map;
    }



    private static (string? Version, byte[]? Exec) ReadIpa(ZipArchive zip) {
        var plist = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".app/Info.plist", StringComparison.OrdinalIgnoreCase));
        if (plist is null) return (null, null);

        string plistText;
        using (var r = new StreamReader(plist.Open())) plistText = r.ReadToEnd();
        var version = PlistString(plistText, "CFBundleShortVersionString");
        if (version is null) return (null, null);

        var execEntry = zip.Entries.FirstOrDefault(IsIosAppExecutable);
        if (execEntry is null) return (version, null);
        using var es = execEntry.Open();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        return (version, ms.ToArray());
    }

    private static bool IsIosAppExecutable(ZipArchiveEntry e) {
        var f = e.FullName;
        if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
        var appIdx = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (appIdx < 0) return false;
        var rest = f[(appIdx + 5)..];
        return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
    }



    private static string? PlistString(string plist, string key) {
        var k = plist.IndexOf($"<key>{key}</key>", StringComparison.Ordinal);
        if (k < 0) return null;
        var s = plist.IndexOf("<string>", k, StringComparison.Ordinal);
        if (s < 0) return null;
        s += "<string>".Length;
        var e = plist.IndexOf("</string>", s, StringComparison.Ordinal);
        return e < 0 ? null : plist[s..e].Trim();
    }


    private static (int, int, int, int) VersionKey(string v) {
        var p = v.Split('.');
        int N(int i) => i < p.Length && int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        return (N(0), N(1), N(2), N(3));
    }
}
