using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

// Pulls the Egg Inc native binary out of a mobile app archive (Android APK or iOS IPA, both zips) and
// carves the embedded FileDescriptorProto from it. The descriptor lives in the native binary, which is a
// COMPRESSED zip entry, so a raw scan of the archive bytes will not find it - we must locate + decompress
// the binary entry first. Candidate entries, in priority order:
//   APK: lib/<abi>/libegginc.so  (prefer arm64), then any lib .so
//   IPA: Payload/<App>.app/<exec> Mach-O (the executable + frameworks)
// Each candidate is decompressed (size-capped against zip bombs - this runs on a public endpoint over
// attacker-supplied bytes) and run through DescriptorProtoCarver; the first that yields a descriptor wins.
// Pure + defensive: the archive is read as bytes, never executed.
public static class ArchiveProtoExtractor
{
    // Cap on a single decompressed entry (real binaries: libegginc.so ~10-40MB, iOS Mach-O ~50-90MB).
    private const long MaxEntryBytes = 300_000_000L;

    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes)
    {
        if (archiveZipBytes is null || archiveZipBytes.Length == 0)
            return new DescriptorProtoCarver.ExtractResult(false, null, "empty archive", null, []);

        foreach (var entryBytes in CandidateBinaries(archiveZipBytes))
        {
            var r = DescriptorProtoCarver.Extract(entryBytes);
            if (r.Ok) return r;
        }

        // Last resort: scan the whole archive bytes (covers a stored/uncompressed binary entry).
        return DescriptorProtoCarver.Extract(archiveZipBytes);
    }

    // Yields the decompressed bytes of each candidate binary entry, best-guess order, APK then IPA.
    private static IEnumerable<byte[]> CandidateBinaries(byte[] archiveZipBytes)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(archiveZipBytes, writable: false), ZipArchiveMode.Read);
        }
        catch
        {
            yield break; // not a readable zip
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

    // Ranks zip entries by how likely they hold the descriptor. APK native libs + the iOS app executable
    // and its frameworks, arm64 preferred. Distinct, highest-priority first.
    private static IEnumerable<ZipArchiveEntry> OrderedCandidates(ZipArchive zip)
    {
        bool IsApkLibEggInc(ZipArchiveEntry e) => e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase);
        bool IsArm64So(ZipArchiveEntry e) => e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
        bool IsAnySo(ZipArchiveEntry e) => e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase);
        // The iOS app executable has no extension and sits directly under Payload/<App>.app/.
        bool IsIosAppExecutable(ZipArchiveEntry e)
        {
            var f = e.FullName;
            if (!f.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase)) return false;
            var appIdx = f.IndexOf(".app/", StringComparison.OrdinalIgnoreCase);
            if (appIdx < 0) return false;
            var rest = f[(appIdx + 5)..];
            return rest.Length > 0 && !rest.Contains('/') && !rest.Contains('.'); // top-level, extensionless
        }
        // iOS frameworks (nanopb/proto runtime + the app's own) carry descriptors too.
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

// Back-compat alias. Earlier code + tests referenced ApkProtoExtractor; the logic now handles both APK
// and IPA via ArchiveProtoExtractor.
public static class ApkProtoExtractor
{
    public static DescriptorProtoCarver.ExtractResult Extract(byte[] archiveZipBytes) =>
        ArchiveProtoExtractor.Extract(archiveZipBytes);
}
