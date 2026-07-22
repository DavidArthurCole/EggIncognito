using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;


public sealed class ShipShellDownloader(IHttpClientFactory httpFactory, ILogger<ShipShellDownloader> logger) {
    private static readonly string[] AllowedHosts = ["auxbrain.com", "www.auxbrain.com"];



    public async Task<RpoMeshDecoder.DecodeResult> DownloadAndDecodeAsync(string url, string? name, CancellationToken ct) {
        if (!IsAllowed(url))
            return RpoMeshDecoder.Decode([], name) with { Diagnostics = $"refused: host not in CDN allowlist ({url})" };
        try {
            var client = httpFactory.CreateClient("inspector");
            var bytes = await client.GetByteArrayAsync(url, ct);
            var decode = RpoMeshDecoder.Decode(bytes, name);
            logger.LogInformation("shell mesh {Url} -> {Bytes}B, decode {Ok}", url, bytes.Length, decode.Ok);
            return decode;
        } catch (Exception ex) {
            logger.LogWarning(ex, "shell mesh {Url} -> download FAILED", url);
            return RpoMeshDecoder.Decode([], name) with { Diagnostics = ex.Message };
        }
    }

    private static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
        && AllowedHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase);
}
