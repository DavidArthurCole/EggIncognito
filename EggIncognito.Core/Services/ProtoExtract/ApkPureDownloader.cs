using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;

// Downloads + unzips the arm64_v8a split from APKPure. Moved from ApkPureSource so the runner (no web
// deps) can reuse it. Dependency-free: takes a plain HttpClient, swallows failures to null (Core has no
// ILogger here). ExtractArmSplit is pure and the single source of truth; ApkPureSource delegates to it.
public sealed class ApkPureDownloader(HttpClient http)
{
    private const string Package = "com.auxbrain.egginc";
    private const string Base = "https://apkpure.com";

    public async Task<byte[]?> DownloadApkAsync(string appVersion, CancellationToken ct = default)
    {
        try
        {
            var url = $"{Base}/egg-inc/{Package}/download/{Uri.EscapeDataString(appVersion)}";
            var res = await http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            return await res.Content.ReadAsByteArrayAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    // Pulls the arm64_v8a split apk bytes out of an APKPure XAPK (zip-of-apks). Pure, no network. Returns
    // null if the blob is not a zip-of-apks, or is a single base APK with no arm split. Matches both
    // spellings: APKPure config.arm64_v8a.apk, adb-pull split_config.arm64_v8a.apk. Base is excluded.
    public static byte[]? ExtractArmSplit(byte[] downloaded)
    {
        if (downloaded is null || downloaded.Length == 0) return null;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(downloaded, writable: false), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var name = entry.Name; // file name only, no zip path prefix
                if (name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                    && name.Contains("arm64_v8a", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals($"{Package}.apk", StringComparison.OrdinalIgnoreCase))
                {
                    using var s = entry.Open();
                    using var ms = new MemoryStream();
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            return null;
        }
        catch (InvalidDataException)
        {
            // Not a zip; a bare base.apk parses but carries no arm64_v8a entry, also null.
            return null;
        }
    }
}
