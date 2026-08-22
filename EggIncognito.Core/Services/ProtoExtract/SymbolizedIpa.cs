using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

public static class SymbolizedIpa {
    public static (string? Version, byte[]? Exec) Read(ZipArchive zip) {
        var plist = zip.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)
            && e.FullName.EndsWith(".app/Info.plist", StringComparison.OrdinalIgnoreCase));
        if (plist is null) return (null, null);

        string plistText;
        using (var r = new StreamReader(plist.Open())) plistText = r.ReadToEnd();
        string? version = PlistString(plistText, "CFBundleShortVersionString");
        if (version is null) return (null, null);

        var execEntry = zip.Entries.FirstOrDefault(IsIosAppExecutable);
        if (execEntry is null) return (version, null);
        using var es = execEntry.Open();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        return (version, ms.ToArray());
    }

    public static (string? Version, byte[]? Exec) Read(byte[] ipaBytes) {
        try {
            using var ms = new MemoryStream(ipaBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            return Read(zip);
        } catch {
            return (null, null);
        }
    }

    private static bool IsIosAppExecutable(ZipArchiveEntry e) {
        string f = e.FullName;
        if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
        int appIdx = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
        if (appIdx < 0) return false;
        string rest = f[(appIdx + 5)..];
        return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
    }

    private static string? PlistString(string plist, string key) {
        int k = plist.IndexOf($"<key>{key}</key>", StringComparison.Ordinal);
        if (k < 0) return null;
        int s = plist.IndexOf("<string>", k, StringComparison.Ordinal);
        if (s < 0) return null;
        s += "<string>".Length;
        int e = plist.IndexOf("</string>", s, StringComparison.Ordinal);
        return e < 0 ? null : plist[s..e].Trim();
    }
}
