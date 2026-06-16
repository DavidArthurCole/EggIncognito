using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

// Android-facing entry point: pulls the native lib out of an APK zip and carves the embedded
// FileDescriptorProto from it (Android links protobuf with descriptor support, same as iOS). Tries the
// per-arch libegginc.so first, then any lib/*/*.so, then the raw APK bytes as a last resort. Pure +
// defensive: the APK is read as bytes, never executed. Reuses DescriptorProtoCarver for the carve.
public static class ApkProtoExtractor
{
    // Cap on a single decompressed native lib (the real libegginc.so is ~10-40MB); rejects zip bombs.
    private const long MaxLibBytes = 300_000_000L;

    public static DescriptorProtoCarver.ExtractResult Extract(byte[] apkZipBytes)
    {
        if (apkZipBytes is null || apkZipBytes.Length == 0)
            return new DescriptorProtoCarver.ExtractResult(false, null, "empty apk", null, []);

        byte[]? lib = TryReadNativeLib(apkZipBytes);
        if (lib is not null)
        {
            var r = DescriptorProtoCarver.Extract(lib);
            if (r.Ok) return r;
        }

        // Last resort: scan the whole APK (the descriptor may sit in a different entry, and the carver's
        // anchor search is offset-agnostic). Decompresses nothing extra; just searches the zip bytes.
        return DescriptorProtoCarver.Extract(apkZipBytes);
    }

    // Reads the most relevant native lib bytes from the APK: prefer libegginc.so, then any arm64 .so,
    // then any .so. Returns null when the zip has no native libs or cannot be opened.
    private static byte[]? TryReadNativeLib(byte[] apkZipBytes)
    {
        try
        {
            using var ms = new MemoryStream(apkZipBytes, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase))
                     ?? zip.Entries.FirstOrDefault(e => e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                            && e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                     ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            // Reject a decompression bomb: this runs on a public endpoint over attacker-supplied bytes.
            // The declared uncompressed size must be sane; a real libegginc.so is well under this.
            if (entry.Length is <= 0 or > MaxLibBytes) return null;
            using var es = entry.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return buf.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
