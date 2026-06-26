using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Serves the shell DB built from the locally stored game config (GameConfigStore -> DLCCatalog ->
// ShellCatalog). A shell is a cosmetic mesh for an asset type; the viewer lists shells, optionally filtered
// to a model's asset type, and fetches a shell's .glb (download the CDN .rpoz + decode + optional animate).
// Reads are public; the glb fetch does CDN egress so it is egress-gated + hosted-auth like Inspector send.
[ApiController]
[Route("api/shells")]
public sealed class ShellsController(
    GameConfigStore configStore, ShipShellDownloader downloader, MeshAssetCache cache,
    IAppMode appMode, ICurrentUser currentUser) : ControllerBase
{
    // Lists shells from the stored config, optionally for one asset type (?assetType=CHICKEN) and platform
    // (?platform=ios, default ios). Returns identifier/name/assetType/url so the viewer can pick one. Public.
    [HttpGet]
    public IActionResult List([FromQuery] string platform = "ios", [FromQuery] string? assetType = null)
    {
        var catalog = LoadCatalog(platform);
        if (catalog is null) return Ok(new { ok = false, diagnostics = $"no stored config for {platform}; ingest one via /api/config" });

        var shells = string.IsNullOrEmpty(assetType)
            ? ShellCatalog.FromCatalog(catalog)
            : ShellCatalog.ForAssetType(catalog, assetType);

        // Group asset types for the picker (which models have shells), plus the shells themselves.
        var assetTypes = ShellCatalog.FromCatalog(catalog)
            .Select(s => s.AssetType).Distinct(StringComparer.Ordinal).OrderBy(a => a, StringComparer.Ordinal).ToArray();

        return Ok(new
        {
            ok = true,
            platform,
            assetTypes,
            count = shells.Count,
            shells = shells.Select(s => new { s.Identifier, s.Name, s.AssetType, s.Url, s.ModifiedGeometry }),
        });
    }

    // Fetches one shell's mesh as .glb: resolve its url from the catalog, download + decode, optional animate.
    // Caches the decoded glb (platform "shell") so repeat views skip the CDN. Egress + hosted-auth gated.
    [HttpGet("{platform}/{identifier}/glb")]
    [EnableRateLimiting("egress")]
    public async Task<IActionResult> Glb(string platform, string identifier, [FromQuery] string? animate, [FromQuery] float seconds, CancellationToken ct)
    {
        if (appMode.Mode == AppMode.Hosted && !currentUser.IsAuthenticated)
            return StatusCode(403, new { error = "log in to download shell meshes from the hosted site" });

        var cacheKey = $"{platform}_{identifier}";
        var glb = cache.TryGet("shell", cacheKey);
        if (glb is null)
        {
            var catalog = LoadCatalog(platform);
            if (catalog is null) return NotFound(new { error = $"no stored config for {platform}" });
            var shell = ShellCatalog.ById(catalog, identifier);
            if (shell is null) return NotFound(new { error = "unknown shell identifier" });

            var decode = await downloader.DownloadAndDecodeAsync(shell.Url, identifier, ct);
            if (!decode.Ok) return Ok(new { ok = false, diagnostics = decode.Diagnostics });
            glb = decode.Glb!;
            await cache.PutAsync("shell", cacheKey, glb, ct);
        }

        if (!string.IsNullOrEmpty(animate))
        {
            var opts = new Services.Assets.GltfAnimator.Options(
                Services.Assets.GltfAnimator.ParseKind(animate), seconds > 0 ? seconds : 6f);
            var anim = Services.Assets.GltfAnimator.Animate(glb, opts);
            if (anim.Ok) glb = anim.Glb!;
        }
        return File(glb, "model/gltf-binary", $"{identifier}.glb");
    }

    // Parses the stored ConfigResponse JSON back to a proto (JsonParser, the sanctioned codec) and returns
    // its DLCCatalog. Null when no config is stored for the platform or it carries no catalog.
    private Ei.DLCCatalog? LoadCatalog(string platform)
    {
        var stored = configStore.Get(platform);
        if (stored is null) return null;
        try
        {
            var cfg = Ei.ConfigResponse.Parser.ParseJson(stored.Json);
            return cfg.DlcCatalog;
        }
        catch { return null; }
    }
}
