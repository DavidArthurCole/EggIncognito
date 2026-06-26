using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Resolves + downloads the 4 orbital ship meshes (Galeggtica, Defihent, Voyegger, Henerprise) that are NOT
// bundled in the app. They live on auxbrain's CDN; their ids come only from a DLCCatalog (the game's
// ei/get_config response). The caller posts a base64 ConfigResponse (captured or from a live get_config);
// this resolves each ship's CDN url, downloads it, decodes to .glb, and returns the export merged into the
// Spaceship-enum keyspace. Egress to auxbrain only - gated like Inspector Live send (egress limiter +
// hosted-auth). This closes the 4-ship coverage gap left by the bundle-only extraction path.
[ApiController]
[Route("api/ship-assets")]
public sealed class ShipAssetsController(
    ShipShellDownloader downloader, ITransportPipeline pipeline, IHttpClientFactory httpFactory,
    IAppMode appMode, ICurrentUser currentUser, IConfiguration config,
    ILogger<ShipAssetsController> logger) : ControllerBase
{
    // Where the game's config (with the DLCCatalog) lives. www host is auxbrain-allowlisted.
    private const string GetConfigUrl = "https://www.auxbrain.com/ei/get_config";

    public sealed record ResolveRequest(string ConfigResponseBase64);

    // Manual path: caller supplies a base64 ConfigResponse (a capture or a hand-pulled get_config). Useful
    // when egress signing is unavailable (no salt) or to resolve from an archived catalog.
    [HttpPost("resolve-from-config")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> ResolveFromConfig([FromBody] ResolveRequest body, [FromQuery] bool write, CancellationToken ct)
    {
        if (HostedGate() is { } gate) return gate;

        Ei.ConfigResponse config;
        try
        {
            var bytes = ProtoFraming.FromBase64Loose(body.ConfigResponseBase64 ?? "");
            // Tolerate a wrapped AuthenticatedMessage (a raw capture) or a bare ConfigResponse.
            var inner = ProtoFraming.TryUnwrap(bytes) ?? bytes;
            config = Ei.ConfigResponse.Parser.ParseFrom(inner);
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, diagnostics = $"could not parse ConfigResponse: {ex.Message}" });
        }

        return await ResolveAndDownloadAsync(config, write, ct);
    }

    // Automatic path: the server calls auxbrain ei/get_config itself (signing the ConfigRequest with the
    // instance salt), decodes the ConfigResponse, and resolves+downloads the 4 CDN ships - no manual
    // capture step. Needs a signing salt (EGG_INC_API_SALT); without it the request is unsigned and the API
    // will reject it, so we fail fast with a clear message.
    [HttpPost("pull-from-live")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> PullFromLive([FromQuery] bool write, CancellationToken ct)
    {
        if (HostedGate() is { } gate) return gate;
        if (!pipeline.CanSign)
            return StatusCode(503, new { error = "live get_config needs a signing salt (EGG_INC_API_SALT); use resolve-from-config with a captured ConfigResponse instead" });

        // Minimal signed ConfigRequest. The DLCCatalog is global config, so a bare rinfo suffices.
        var req = new Ei.ConfigRequest { Rinfo = new Ei.BasicRequestInfo() };
        var built = pipeline.Build(req.ToByteArray(), wrap: true);

        var client = httpFactory.CreateClient("inspector");
        string rawBody;
        try
        {
            var content = new StringContent(built.FinalFormBody,
                System.Text.Encoding.UTF8, "application/x-www-form-urlencoded");
            var resp = await client.PostAsync(GetConfigUrl, content, ct);
            rawBody = (await resp.Content.ReadAsStringAsync(ct)).Trim();
            if (!resp.IsSuccessStatusCode)
                return Ok(new { ok = false, diagnostics = $"get_config -> HTTP {(int)resp.StatusCode}" });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ship-assets: live get_config FAILED");
            return Ok(new { ok = false, diagnostics = $"get_config request failed: {ex.Message}" });
        }

        Ei.ConfigResponse config;
        try
        {
            // get_config returns a wrapped AuthenticatedMessage; unwrap to the inner ConfigResponse.
            var raw = ProtoFraming.FromBase64Loose(rawBody);
            var inner = ProtoFraming.TryUnwrap(raw) ?? raw;
            config = Ei.ConfigResponse.Parser.ParseFrom(inner);
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, diagnostics = $"could not decode get_config response: {ex.Message}" });
        }

        return await ResolveAndDownloadAsync(config, write, ct);
    }

    // Egress from this server: on hosted, only for a signed-in user (mirrors Inspector /send). Returns the
    // 403 result to short-circuit, or null when allowed.
    private IActionResult? HostedGate() =>
        appMode.Mode == AppMode.Hosted && !currentUser.IsAuthenticated
            ? StatusCode(403, new { error = "log in to download ship meshes from the hosted site" })
            : null;

    // Shared: resolve the 4 CDN ships from a DLCCatalog, download+decode each, and shape the enum-keyed
    // response (same as the bundled export, plus per-ship source url + an explicit unresolved list).
    private async Task<IActionResult> ResolveAndDownloadAsync(Ei.ConfigResponse config, bool write, CancellationToken ct)
    {
        if (config.DlcCatalog is null)
            return Ok(new { ok = false, diagnostics = "ConfigResponse has no dlc_catalog" });

        var cdnShips = ShipNameMap.All
            .Where(s => s.BundleStem is null && s.ShellAsset is not null)
            .ToList();
        var afxNames = cdnShips.Select(s => s.ShellAsset!).ToList();

        var shells = ShipShellResolver.Resolve(config.DlcCatalog, afxNames);
        logger.LogInformation("ship-assets: resolved {Found}/{Want} CDN ship shells", shells.Count, afxNames.Count);
        var downloaded = await downloader.DownloadAsync(shells, ct);

        var afxToEnum = cdnShips.ToDictionary(s => s.ShellAsset!, s => s.EnumName, StringComparer.OrdinalIgnoreCase);
        var ok = downloaded.Where(d => d.Decode.Ok)
            .Select(d => (EnumName: afxToEnum[d.AfxName], d.Url, Glb: d.Decode.Glb!, Bounds: d.Decode.Bounds!))
            .ToList();

        // Write the resolved ships into the asset-repo layout (ships/<EnumName>.glb) when requested + a path
        // is configured + writes are enabled. These slot beside the bundled ships' output; the manifest is
        // owned by the bundle export, so here we write only the glb files (the admin button's "do it").
        var (wrote, dir) = await MaybeWriteCdnShipsAsync(ok, write, ct);

        var ships = ok.Select(s => new
        {
            enumName = s.EnumName,
            file = $"ships/{s.EnumName}.glb",
            url = s.Url,
            sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(s.Glb)),
            bbox = new { min = new[] { s.Bounds.Min.X, s.Bounds.Min.Y, s.Bounds.Min.Z }, max = new[] { s.Bounds.Max.X, s.Bounds.Max.Y, s.Bounds.Max.Z } },
            glbBase64 = Convert.ToBase64String(s.Glb),
        }).ToList();

        var unresolved = cdnShips
            .Where(s => !ships.Any(sh => sh.enumName == s.EnumName))
            .Select(s => new
            {
                enumName = s.EnumName,
                afxName = s.ShellAsset,
                diagnostics = downloaded.FirstOrDefault(d => d.AfxName == s.ShellAsset)?.Error
                              ?? "no matching DLCItem in the catalog",
            }).ToList();

        return Ok(new { ok = ships.Count > 0, count = ships.Count, ships, unresolved, wroteToDisk = wrote, outputDir = wrote ? dir : null });
    }

    // Writes the resolved CDN ship glbs to ShipAssets:OutputDir/ships/ when write=true + the path is set +
    // writes are enabled. Returns (wrote, dir). Gated by CanWrite so a hosted instance never writes shared disk.
    private async Task<(bool, string?)> MaybeWriteCdnShipsAsync(
        IReadOnlyList<(string EnumName, string Url, byte[] Glb, RpoMeshDecoder.BBox Bounds)> ships, bool write, CancellationToken ct)
    {
        if (!write || ships.Count == 0) return (false, null);
        if (!appMode.CanWrite) return (false, null);
        var dir = config["ShipAssets:OutputDir"];
        if (string.IsNullOrEmpty(dir)) return (false, null);

        var shipsDir = Path.Combine(dir, "ships");
        Directory.CreateDirectory(shipsDir);
        foreach (var s in ships)
            await System.IO.File.WriteAllBytesAsync(Path.Combine(shipsDir, $"{s.EnumName}.glb"), s.Glb, ct);
        return (true, dir);
    }
}
