using System.IO.Compression;
using System.Net;
using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class ArchiveProtoExtractor {
    private const long MaxEntryBytes = 300_000_000L;

    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes) {
        if (archiveZipBytes is null || archiveZipBytes.Length == 0)
            return new DescriptorProtoCarver.ExtractResult(false, null, "empty archive", null, []);

        if (TryReadArmBundle(archiveZipBytes, out byte[] armApk, out byte[]? baseApk)) {
            (string? bv, string? bb) = ReadVersion(baseApk ?? armApk);
            var inner = Extract(armApk);
            return inner.Ok ? inner with { AppVersion = bv, Build = bb } : inner;
        }

        (string? appVersion, string? build) = ReadVersion(archiveZipBytes);

        foreach (byte[] entryBytes in CandidateBinaries(archiveZipBytes)) {
            var r = DescriptorProtoCarver.Extract(entryBytes);
            if (r.Ok) return r with { AppVersion = appVersion, Build = build };
        }


        var raw = DescriptorProtoCarver.Extract(archiveZipBytes);
        return raw.Ok ? raw with { AppVersion = appVersion, Build = build } : raw;
    }

    private static bool TryReadArmBundle(byte[] zipBytes, out byte[] armApk, out byte[]? baseApk) {
        armApk = [];
        baseApk = null;
        try {
            using var ms = new MemoryStream(zipBytes, false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var arm = zip.Entries.FirstOrDefault(e =>
                e.Name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                && e.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase));
            if (arm is null) return false;

            armApk = ReadEntry(arm);
            var baseEntry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                || e.FullName.EndsWith("/base.apk", StringComparison.OrdinalIgnoreCase));
            baseApk = baseEntry is null ? null : ReadEntry(baseEntry);
            return armApk.Length > 0;
        } catch {
            return false;
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry) {
        using var es = entry.Open();
        using var buf = new MemoryStream();
        es.CopyTo(buf);
        return buf.ToArray();
    }


    private static (string? AppVersion, string? Build) ReadVersion(byte[] zipBytes) {
        try {
            using var ms = new MemoryStream(zipBytes, false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            var plist = zip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".app/Info.plist", StringComparison.OrdinalIgnoreCase));
            if (plist is not null) {
                using var es = plist.Open();
                using var buf = new MemoryStream();
                es.CopyTo(buf);
                string text = Encoding.UTF8.GetString(buf.ToArray());
                string? shortVer = PlistString(text, "CFBundleShortVersionString");

                return (shortVer, null);
            }

            var manifest = zip.GetEntry("AndroidManifest.xml");
            if (manifest is not null) {
                using var es = manifest.Open();
                using var buf = new MemoryStream();
                es.CopyTo(buf);
                byte[] axml = buf.ToArray();
                return (ApkVersionCode.ReadVersionName(axml), ApkVersionCode.ParseAxml(axml));
            }
        } catch {
            /* metadata is best-effort; extraction does not depend on it */
        }

        return (null, null);
    }


    private static string? PlistString(string plistXml, string key) {
        string keyTag = $"<key>{key}</key>";
        int ki = plistXml.IndexOf(keyTag, StringComparison.Ordinal);
        if (ki < 0) return null;
        int open = plistXml.IndexOf("<string>", ki + keyTag.Length, StringComparison.Ordinal);
        if (open < 0) return null;
        int start = open + "<string>".Length;
        int close = plistXml.IndexOf("</string>", start, StringComparison.Ordinal);
        if (close < 0) return null;
        string val = plistXml[start..close].Trim();
        return val.Length == 0 ? null : WebUtility.HtmlDecode(val);
    }


    private static IEnumerable<byte[]> CandidateBinaries(byte[] archiveZipBytes) {
        ZipArchive zip;
        try {
            zip = new ZipArchive(new MemoryStream(archiveZipBytes, false), ZipArchiveMode.Read);
        } catch {
            yield break;
        }

        using (zip) {
            foreach (var entry in OrderedCandidates(zip)) {
                if (entry.Length is <= 0 or > MaxEntryBytes) continue;
                byte[] bytes;
                try {
                    using var es = entry.Open();
                    using var buf = new MemoryStream();
                    es.CopyTo(buf);
                    bytes = buf.ToArray();
                } catch {
                    continue;
                }

                yield return bytes;
            }
        }
    }


    private static IEnumerable<ZipArchiveEntry> OrderedCandidates(ZipArchive zip) {
        bool IsArm64LibEggInc(ZipArchiveEntry e) {
            return e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase)
                   && e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase);
        }

        bool IsApkLibEggInc(ZipArchiveEntry e) {
            return e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase);
        }

        bool IsArm64So(ZipArchiveEntry e) {
            return e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase) &&
                   e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
        }

        bool IsAnySo(ZipArchiveEntry e) {
            return e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
        }

        bool IsIosAppExecutable(ZipArchiveEntry e) {
            string f = e.FullName;
            if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
            int appIdx = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (appIdx < 0) return false;
            string rest = f[(appIdx + 5)..];
            return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.');
        }

        bool IsIosFrameworkBinary(ZipArchiveEntry e) {
            string f = e.FullName;
            return f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)
                   && f.Contains(".framework/", StringComparison.OrdinalIgnoreCase)
                   && !f.EndsWith('/') && !e.Name.Contains('.');
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pred in new[]
                     { IsArm64LibEggInc, IsApkLibEggInc, IsArm64So, IsAnySo, IsIosAppExecutable, IsIosFrameworkBinary }) {
            foreach (var e in zip.Entries) {
                if (pred(e) && seen.Add(e.FullName))
                    yield return e;
            }
        }
    }
}

public static class ApkProtoExtractor {
    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes) =>
        ArchiveProtoExtractor.Extract(archiveZipBytes);
}
