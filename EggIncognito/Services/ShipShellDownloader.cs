using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

// Downloads the CDN-hosted orbital-ship meshes (the 4 ships not bundled in the app) and decodes them to
// .glb. ShipShellResolver turns a live/captured DLCCatalog into per-ship CDN URLs; this fetches each over
// the egress HttpClient and runs RpoMeshDecoder. Egress to auxbrain's CDN only - the caller gates it the
// same way Inspector's Live send is gated (egress limiter + hosted auth). Returns one result per ship,
// decoded or with a diagnostic, never throws.
public sealed class ShipShellDownloader(IHttpClientFactory httpFactory, ILogger<ShipShellDownloader> logger)
{
    // The auxbrain mesh CDN. The resolver only ever produces URLs under this host (or an absolute DLCItem.url
    // that the game itself supplied); anything else is refused so this is not an open proxy.
    private static readonly string[] AllowedHosts = ["auxbrain.com", "www.auxbrain.com"];

    public sealed record DownloadedShip(string AfxName, string Url, RpoMeshDecoder.DecodeResult Decode, string? Error);

    public async Task<IReadOnlyList<DownloadedShip>> DownloadAsync(
        IEnumerable<ShipShellResolver.ShipShell> shells, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("inspector");
        var results = new List<DownloadedShip>();

        foreach (var shell in shells)
        {
            if (!IsAllowed(shell.Url))
            {
                results.Add(new DownloadedShip(shell.AfxName, shell.Url,
                    RpoMeshDecoder.Decode([]), $"refused: host not in CDN allowlist ({shell.Url})"));
                continue;
            }

            try
            {
                var bytes = await client.GetByteArrayAsync(shell.Url, ct);
                // The mapped enum name is unknown here (resolver works in afx space); RpoMeshDecoder takes an
                // optional cosmetic name, so pass the afx name. The caller renames to the enum on export.
                var decode = RpoMeshDecoder.Decode(bytes, shell.AfxName);
                logger.LogInformation("ship shell {Afx} {Url} -> {Bytes}B, decode {Ok}",
                    shell.AfxName, shell.Url, bytes.Length, decode.Ok);
                results.Add(new DownloadedShip(shell.AfxName, shell.Url, decode, decode.Ok ? null : decode.Diagnostics));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ship shell {Afx} {Url} -> download FAILED", shell.AfxName, shell.Url);
                results.Add(new DownloadedShip(shell.AfxName, shell.Url, RpoMeshDecoder.Decode([]), ex.Message));
            }
        }
        return results;
    }

    // Downloads + decodes ONE mesh by absolute CDN url (a shell's DLCItem url). Returns the decode result;
    // a refused host or download error yields a failed decode with a diagnostic, never throws. Used by the
    // shell viewer to fetch any catalog shell mesh.
    public async Task<RpoMeshDecoder.DecodeResult> DownloadAndDecodeAsync(string url, string? name, CancellationToken ct)
    {
        if (!IsAllowed(url))
            return RpoMeshDecoder.Decode([], name) with { Diagnostics = $"refused: host not in CDN allowlist ({url})" };
        try
        {
            var client = httpFactory.CreateClient("inspector");
            var bytes = await client.GetByteArrayAsync(url, ct);
            var decode = RpoMeshDecoder.Decode(bytes, name);
            logger.LogInformation("shell mesh {Url} -> {Bytes}B, decode {Ok}", url, bytes.Length, decode.Ok);
            return decode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "shell mesh {Url} -> download FAILED", url);
            return RpoMeshDecoder.Decode([], name) with { Diagnostics = ex.Message };
        }
    }

    private static bool IsAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
        && AllowedHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase);
}
