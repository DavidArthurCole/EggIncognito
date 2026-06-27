using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Serves the farm-environment meshes + the presets that compose them into a playground backdrop. The meshes
// are NOT shipped: they are pulled off a connected device's bundle on demand and cached (DeviceMeshProvider),
// the same way every other game mesh is sourced. The env catalog is just names + layout (no asset bytes).
// Presets read is public; the glb pull does a device round-trip so it is admin-gated like the device-mesh
// route. Without a reachable device the glb pull returns 503 (the catalog still lists what is available).
[ApiController]
[Route("api/env")]
public sealed class EnvController(DeviceMeshProvider meshes, ICurrentUser currentUser) : ControllerBase
{
    // The placeable env catalog (buildings + habs), for the designer's Add-element picker. Public, names only.
    [HttpGet("catalog")]
    public IActionResult Catalog() => Ok(new
    {
        pieces = EnvCatalog.Pieces.Select(p => new { p.Stem, p.Label }),
        habs = EnvCatalog.Habs.Select(p => new { p.Stem, p.Label }),
    });

    // One env mesh decoded to glb, by stem (allowlisted). Pulled off the asset-source device, cache-first.
    // Admin-gated (device round-trip). ?device= picks a specific source device, else first reachable.
    [HttpGet("{stem}/glb")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Glb(string stem, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (!EnvCatalog.IsKnownPiece(stem)) return NotFound(new { error = "unknown env mesh" });

        var res = await meshes.GetGlbAsync(stem, device, ct);
        if (!res.Ok) return StatusCode(res.Status, new { error = res.Diagnostics });
        return File(res.Glb!, "model/gltf-binary", $"{stem}.glb");
    }
}
