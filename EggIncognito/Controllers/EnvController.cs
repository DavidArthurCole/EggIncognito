using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Serves the farm-environment meshes shipped with the app (Assets/env/*.rpo) as glb, plus the presets that
// compose them into a playground backdrop. Local files only (no egress, no device), so reads are public and
// work on the hosted site. Decoded glbs are cached under platform "env" like every other mesh.
[ApiController]
[Route("api/env")]
public sealed class EnvController(IWebHostEnvironment env, MeshAssetCache cache, ILogger<EnvController> logger) : ControllerBase
{
    private const string CachePlatform = "env";

    // The available env pieces + presets, for the playground env widget.
    [HttpGet("presets")]
    public IActionResult Presets() => Ok(new
    {
        pieces = EnvCatalog.Pieces.Select(p => new { p.Stem, p.Label }),
        presets = EnvCatalog.Presets.Select(p => new
        {
            p.Id, p.Label,
            pieces = p.Pieces.Select(pp => new { pp.Stem, pp.Offset }),
        }),
        habs = EnvCatalog.Habs.Select(p => new { p.Stem, p.Label }),
    });

    // One env mesh decoded to glb, by stem (allowlisted). Cache-first; decodes the shipped .rpo on a miss.
    [HttpGet("{stem}/glb")]
    public async Task<IActionResult> Glb(string stem, CancellationToken ct)
    {
        if (!EnvCatalog.IsKnownPiece(stem)) return NotFound(new { error = "unknown env mesh" });

        var glb = cache.TryGet(CachePlatform, stem);
        if (glb is null)
        {
            var path = Path.Combine(env.ContentRootPath, "Assets", "env", stem + ".rpo");
            if (!System.IO.File.Exists(path)) return NotFound(new { error = $"env mesh not shipped: {stem}" });

            byte[] rpo;
            try { rpo = await System.IO.File.ReadAllBytesAsync(path, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "env read failed {Stem}", stem); return StatusCode(500, new { error = "read failed" }); }

            var decode = RpoMeshDecoder.Decode(rpo, stem);
            if (!decode.Ok) return Ok(new { ok = false, diagnostics = decode.Diagnostics });
            glb = decode.Glb!;
            await cache.PutAsync(CachePlatform, stem, glb, ct);
        }
        return File(glb, "model/gltf-binary", $"{stem}.glb");
    }
}
