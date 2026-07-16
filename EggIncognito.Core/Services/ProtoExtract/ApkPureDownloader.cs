using System.IO.Compression;

namespace EggIncognito.Services.ProtoExtract;


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

   
   
   
    public static byte[]? ExtractArmSplit(byte[] downloaded)
    {
        if (downloaded is null || downloaded.Length == 0) return null;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(downloaded, writable: false), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                var name = entry.Name;
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
           
            return null;
        }
    }
}
