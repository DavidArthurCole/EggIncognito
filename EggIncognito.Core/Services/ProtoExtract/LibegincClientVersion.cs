using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;


public static class LibegincClientVersion {
    private const long MaxSoBytes = 300_000_000L;

    public static int? Read(byte[] apkOrSoBytes, int? prevClientVersion) {
        if (prevClientVersion is null) return null;
        try {
            var so = IsZip(apkOrSoBytes) ? ReadSoFromZip(apkOrSoBytes) : apkOrSoBytes;
            if (so is null) return null;
            var text = Elf64.FindSection(so, ".text");
            if (text is null) return null;
            long end = text.FileOffset + text.Size;
            if (text.FileOffset < 0 || text.Size <= 0 || end > so.Length) return null;
            var textBytes = new byte[text.Size];
            Array.Copy(so, text.FileOffset, textBytes, 0, (int)text.Size);
            return Arm64ClientVersionScanner.Scan(textBytes, prevClientVersion.Value).ClientVersion;
        } catch {
            return null;
        }
    }

    private static bool IsZip(byte[] b) =>
        b is { Length: > 4 } && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04;


    private static byte[]? ReadSoFromZip(byte[] zipBytes) {
        try {
            using var zip = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);
            var entry = zip.GetEntry("lib/arm64-v8a/libegginc.so")
                ?? zip.Entries.FirstOrDefault(e =>
                       e.FullName.Contains("arm64", StringComparison.OrdinalIgnoreCase)
                       && e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase))
                ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/libegginc.so", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length is <= 0 or > MaxSoBytes) return null;
            using var es = entry.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            return buf.ToArray();
        } catch {
            return null;
        }
    }
}
