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
public sealed class EnvController(DeviceMeshProvider meshes, ICurrentUser currentUser, GameBinaryProvider binaries) : ControllerBase
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
    public async Task<IActionResult> FarmLayout([FromQuery] string hab = "hab_10k", [FromQuery] string? device = null, CancellationToken ct = default)
    {
        var stem = EnvCatalog.IsKnownPiece(hab) ? hab : "hab_10k";
        var layout = await RecoveredOrFallbackLayout(stem, device, ct);
        var placed = layout
            .Where(p => EnvCatalog.IsKnownPiece(p.Stem))
            .Select(p => new { p.Stem, label = LabelFor(p.Stem), p.Pos, p.RotY, p.Scale });
        return Ok(new { elements = placed });
    }

    // Use the EXTRACTED singleton placement formulas (evaluated at the farm's half-width) when a symbolized
    // binary is available; otherwise the hand-authored fallback layout. The binary source is best-effort + the
    // recovery never throws, so a missing/stripped binary cleanly falls back. farmHalfWidth is approximated from
    // the standard layout's X-extent (the game derives it from farm-bound state we do not have offline).
    private async Task<IReadOnlyList<EggIncognito.Services.ProtoExtract.FarmLayout.Placed>> RecoveredOrFallbackLayout(
        string stem, string? device, CancellationToken ct)
    {
        try
        {
            var (ok, bin, _) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return EggIncognito.Services.ProtoExtract.FarmLayout.Standard(stem);

            var rec = new EggIncognito.Services.ProtoExtract.FarmLayout.SingletonPlacement(
                EggIncognito.Services.ProtoExtract.Decomp.FarmPlacementRecovery.Recover(bin, "FarmScene17missionControlPos"),
                EggIncognito.Services.ProtoExtract.Decomp.FarmPlacementRecovery.Recover(bin, "FarmScene11fuelTankPos"),
                EggIncognito.Services.ProtoExtract.Decomp.FarmPlacementRecovery.Recover(bin, "FarmScene6hoaPos"));
            const float farmHalfWidth = 13.5f; // approx half the standard farm X-extent; tunable
            return EggIncognito.Services.ProtoExtract.FarmLayout.StandardRecovered(rec, farmHalfWidth, stem);
        }
        catch
        {
            return EggIncognito.Services.ProtoExtract.FarmLayout.Standard(stem);
        }
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

    // Decode stats for a stem's raw .rpo (admin). vertexCount/indexCount/bounds + trailingBytes: nonzero
    // trailing means the file packs more than one mesh (e.g. a hab's floating-effect sub-objects) the
    // single-mesh decoder currently drops. Diagnostic toward multi-mesh extraction.
    [HttpGet("{stem}/decode-stats")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> DecodeStats(string stem, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        var (ok, stats, diag) = await meshes.GetDecodeStatsAsync(stem, device, ct);
        if (!ok || stats is null) return Ok(new { ok = false, diagnostics = diag });
        return Ok(new
        {
            ok = stats.Ok,
            stem,
            vertexCount = stats.VertexCount,
            indexCount = stats.IndexCount,
            trailingBytes = stats.TrailingBytes,
            multiMesh = stats.TrailingBytes > 0,
            bounds = stats.Bounds is null ? null : new
            {
                min = new[] { stats.Bounds.Min.X, stats.Bounds.Min.Y, stats.Bounds.Min.Z },
                max = new[] { stats.Bounds.Max.X, stats.Bounds.Max.Y, stats.Bounds.Max.Z },
            },
            diagnostics = stats.Diagnostics,
        });
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
