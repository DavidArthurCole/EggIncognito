using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;


public static class ArchiveProtoExtractor
{
   
    private const long MaxEntryBytes = 300_000_000L;

    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes)
    {
        if (archiveZipBytes is null || archiveZipBytes.Length == 0)
            return new DescriptorProtoCarver.ExtractResult(false, null, "empty archive", null, []);

        var (appVersion, build) = ReadVersion(archiveZipBytes);

        foreach (var entryBytes in CandidateBinaries(archiveZipBytes))
        {
            var r = DescriptorProtoCarver.Extract(entryBytes);
            if (r.Ok) return r with { AppVersion = appVersion, Build = build };
        }

       
        var raw = DescriptorProtoCarver.Extract(archiveZipBytes);
        return raw.Ok ? raw with { AppVersion = appVersion, Build = build } : raw;
    }

   
   
    private static (string? AppVersion, string? Build) ReadVersion(byte[] zipBytes)
    {
        try
        {
            using var ms = new MemoryStream(zipBytes, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            var plist = zip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".app/Info.plist", StringComparison.OrdinalIgnoreCase));
            if (plist is not null)
            {
                using var es = plist.Open();
                using var buf = new MemoryStream();
                es.CopyTo(buf);
                var text = System.Text.Encoding.UTF8.GetString(buf.ToArray());
                var shortVer = PlistString(text, "CFBundleShortVersionString");
               
                return (shortVer, null);
            }

            var manifest = zip.GetEntry("AndroidManifest.xml");
            if (manifest is not null)
            {
                using var es = manifest.Open();
                using var buf = new MemoryStream();
                es.CopyTo(buf);
                var axml = buf.ToArray();
                return (ApkVersionCode.ReadVersionName(axml), ApkVersionCode.ParseAxml(axml));
            }
        }
        catch { /* metadata is best-effort; extraction does not depend on it */ }
        return (null, null);
    }

   
   
    private static string? PlistString(string plistXml, string key)
    {
        var keyTag = $"<key>{key}</key>";
        var ki = plistXml.IndexOf(keyTag, StringComparison.Ordinal);
        if (ki < 0) return null;
        var open = plistXml.IndexOf("<string>", ki + keyTag.Length, StringComparison.Ordinal);
        if (open < 0) return null;
        var start = open + "<string>".Length;
        var close = plistXml.IndexOf("</string>", start, StringComparison.Ordinal);
        if (close < 0) return null;
        var val = plistXml[start..close].Trim();
        return val.Length == 0 ? null : System.Net.WebUtility.HtmlDecode(val);
    }

   
    private static IEnumerable<byte[]> CandidateBinaries(byte[] archiveZipBytes)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(archiveZipBytes, writable: false), ZipArchiveMode.Read);
        }
        catch
        {
            yield break;
        }

        using (zip)
        {
            foreach (var entry in OrderedCandidates(zip))
            {
                if (entry.Length is <= 0 or > MaxEntryBytes) continue;
                byte[] bytes;
                try
                {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    bytes = buf.ToArray();
                }
                catch { continue; }
                yield return bytes;
            }
        }
    }

   
   
    private static IEnumerable<ZipArchiveEntry> OrderedCandidates(ZipArchive zip)
    {
        bool IsApkLibEggInc(ZipArchiveEntry e) => e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase);
        bool IsArm64So(ZipArchiveEntry e) => e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
        bool IsAnySo(ZipArchiveEntry e) => e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
       
        bool IsIosAppExecutable(ZipArchiveEntry e)
        {
            var f = e.FullName;
            if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
            var appIdx = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (appIdx < 0) return false;
            var rest = f[(appIdx + 5)..];
            return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
        }
       
        bool IsIosFrameworkBinary(ZipArchiveEntry e)
        {
            var f = e.FullName;
            return f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)
                && f.Contains(".framework/", StringComparison.OrdinalIgnoreCase)
                && !f.EndsWith("/") && !e.Name.Contains('.');
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pred in new Func<ZipArchiveEntry, bool>[] { IsApkLibEggInc, IsArm64So, IsAnySo, IsIosAppExecutable, IsIosFrameworkBinary })
            foreach (var e in zip.Entries)
                if (pred(e) && seen.Add(e.FullName))
                    yield return e;
    }
}

public static class ApkProtoExtractor
{
    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes) =>
        ArchiveProtoExtractor.Extract(archiveZipBytes);
}
