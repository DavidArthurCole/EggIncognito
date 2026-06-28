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
        pieces = EnvCatalog.Pieces.Select(p => new { p.Stem, p.Label, p.Group, p.Singleton, p.Family }),
        habs = EnvCatalog.Habs.Select(p => new { p.Stem, p.Label }),
    });

    // The swap-family siblings of a placed piece (hab tiers for a hab, lab levels for a lab). Empty when the
    // piece has no family. Powers the "switch variation" dropdown on a selected element. Public.
    [HttpGet("family/{stem}")]
    public IActionResult Family(string stem) => Ok(new
    {
        family = EnvCatalog.Family(stem).Select(p => new { p.Stem, p.Label }),
    });

    // A game-like default farm layout: the standard farm elements at approximate plot positions, for the
    // designer's one-click "Auto-arrange". ?hab= picks the hab used for the 4-plot row. Public (names + math).
    [HttpGet("farm-layout")]
    public IActionResult FarmLayout([FromQuery] string hab = "hab_10k")
    {
        var stem = EnvCatalog.IsKnownPiece(hab) ? hab : "hab_10k";
        var placed = EggIncognito.Services.ProtoExtract.FarmLayout.Standard(stem)
            .Where(p => EnvCatalog.IsKnownPiece(p.Stem))
            .Select(p => new { p.Stem, label = LabelFor(p.Stem), p.Pos, p.RotY, p.Scale });
        return Ok(new { elements = placed });
    }

    private static string LabelFor(string stem) =>
        EnvCatalog.Pieces.FirstOrDefault(p => p.Stem == stem)?.Label ?? stem;

    // Lists the mesh stems actually present on the asset-source device (Android apk enumeration). Admin-gated
    // (device round-trip). Diagnostic tool to map the env catalog to real on-device asset names. ?filter= is a
    // case-insensitive substring (e.g. ?filter=hab to see every hab mesh the device ships).
    [HttpGet("device-stems")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> DeviceStems([FromQuery] string? device, [FromQuery] string? filter, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        var (ok, stems, diag) = await meshes.ListStemsAsync(device, ct);
        if (!ok) return Ok(new { ok = false, diagnostics = diag });
        var filtered = string.IsNullOrEmpty(filter)
            ? stems
            : stems.Where(s => s.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        return Ok(new { ok = true, count = filtered.Count, stems = filtered });
    }

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
