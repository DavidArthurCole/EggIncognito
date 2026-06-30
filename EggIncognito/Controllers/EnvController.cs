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
            .Select(p => new { p.Stem, label = LabelFor(p.Stem), p.Pos, p.RotY, p.Scale, p.Recenter });
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

    // A hatchery floating sub-piece stem (ei_hatchery_<tier>_<bolt|probe|ring*|top*|middle|orb>). Safe to fetch:
    // no path traversal (allowed chars only) + must be a floating part per HatcheryEffectParts. These are real
    // device meshes that compose the hatchery effect but are not standalone catalog pieces.
    private static bool IsHatcheryFloatingPart(string stem)
    {
        if (string.IsNullOrEmpty(stem) || stem.Length > 64) return false;
        foreach (var c in stem)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        if (!stem.StartsWith("ei_hatchery_", StringComparison.Ordinal)) return false;
        var tier = Services.ProtoExtract.HatcheryEffectParts.TierOf(stem);
        if (tier is null) return false;
        // it is a floating part (not the body) when stripping the tier leaves a recognized floating suffix.
        var body = "ei_hatchery_" + tier;
        return stem.Length > body.Length + 1 && stem.StartsWith(body + "_", StringComparison.Ordinal);
    }

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

    // The hatchery floating effect, resolved from the device mesh list: for each tier (or one requested tier),
    // the body stem + its floating sub-piece stems (bolt/probe/rings/tops). The "floating effect" is these hover
    // meshes, not a particle system (the static binding wall). The playground loads body + parts + orbits the
    // parts. Programmatic via HatcheryEffectParts (no hardcoded per-tier list). Admin-gated (device round-trip).
    [HttpGet("hatchery-effects")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> HatcheryEffects([FromQuery] string? tier, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        var (ok, stems, diag) = await meshes.ListStemsAsync(device, ct);
        if (!ok) return Ok(new { ok = false, diagnostics = diag });

        var tiers = string.IsNullOrWhiteSpace(tier)
            ? Services.ProtoExtract.HatcheryEffectParts.Tiers(stems)
            : [tier];
        var effects = tiers
            .Select(t => Services.ProtoExtract.HatcheryEffectParts.ForTier(stems, t))
            .Where(p => p.Body is not null)
            .Select(p => new { tier = p.Tier, body = p.Body, floating = p.Floating })
            .ToList();
        return Ok(new { ok = true, count = effects.Count, effects });
    }

    // MONOLITHIC hatchery dump: EVERYTHING needed to reproduce the hatchery floating effect for ALL tiers, in ONE
    // call (so it can be tested with a single URL hit). Combines: every tier's body + floating parts (programmatic),
    // each floating piece's decode-stats bounds (the beam/probe geometry), and the binary assembly recovery
    // (FarmScene::updateHatchery anchor + matrix-lambda transforms + rotate_pyramid orbit/beam helper). The effect
    // is a state machine: probes orbit the orb, beams (the spike) fire probe->orb intermittently. Admin-gated.
    [HttpGet("hatchery-dump")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> HatcheryDump([FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });

        // ONE base.apk pull for the whole dump: list every stem, then decode all hatchery pieces from the same
        // in-memory zip. The old loop re-pulled the multi-MB apk off the device once per piece (dozens of pulls,
        // minutes of adb). selectStatsFor derives the pieces-to-decode from the listing itself: every tier's body
        // + floating parts.
        var (sok, stems, stats, sdiag) = await meshes.ListStemsWithStatsAsync(device, HatcheryPieceStems, ct);
        var tiersJson = new System.Text.Json.Nodes.JsonArray();
        if (sok)
        {
            foreach (var tier in Services.ProtoExtract.HatcheryEffectParts.Tiers(stems))
            {
                var parts = Services.ProtoExtract.HatcheryEffectParts.ForTier(stems, tier);
                if (parts.Body is null) continue;
                var pieces = new System.Text.Json.Nodes.JsonArray();
                foreach (var stem in new[] { parts.Body }.Concat(parts.Floating))
                {
                    var st = stats.TryGetValue(stem, out var s) && s.Ok ? s : null;
                    pieces.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["stem"] = stem,
                        ["floating"] = stem != parts.Body,
                        ["vertexCount"] = st?.VertexCount ?? 0,
                        ["bounds"] = st?.Bounds is { } b
                            ? new System.Text.Json.Nodes.JsonObject
                            {
                                ["min"] = new System.Text.Json.Nodes.JsonArray(b.Min.X, b.Min.Y, b.Min.Z),
                                ["max"] = new System.Text.Json.Nodes.JsonArray(b.Max.X, b.Max.Y, b.Max.Z),
                            }
                            : null,
                    });
                }
                tiersJson.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["tier"] = tier, ["body"] = parts.Body,
                    ["floating"] = new System.Text.Json.Nodes.JsonArray(parts.Floating.Select(f => (System.Text.Json.Nodes.JsonNode)f).ToArray()),
                    ["pieces"] = pieces,
                });
            }
        }

        System.Text.Json.Nodes.JsonNode? assembly = null;
        try
        {
            var (bok, bin, bdiag) = await binaries.GetBinaryAsync(device, ct);
            if (bok && bin is not null)
            {
                var asm = Services.ProtoExtract.Decomp.HatcheryAssemblyRecovery.Recover(bin);
                assembly = asm.ToJson();
                ((System.Text.Json.Nodes.JsonObject)assembly)["rotate_pyramid"] = ShapeFn(bin, "FarmScene14rotate_pyramidEP14GameControlleri");
            }
            else assembly = new System.Text.Json.Nodes.JsonObject { ["ok"] = false, ["diagnostics"] = bdiag };
        }
        catch (Exception ex) { assembly = new System.Text.Json.Nodes.JsonObject { ["ok"] = false, ["diagnostics"] = ex.Message }; }

        return Content(new System.Text.Json.Nodes.JsonObject
        {
            ["ok"] = true,
            ["stemsDiagnostics"] = sok ? "ok" : sdiag,
            ["tiers"] = tiersJson,
            ["assembly"] = assembly,
        }.ToJsonString(), "application/json");
    }

    // Every hatchery piece stem to decode for the dump: each tier's body + its floating parts, derived from the
    // full apk stem listing so all decoding happens off one base.apk pull.
    private static IEnumerable<string> HatcheryPieceStems(IReadOnlyList<string> stems)
    {
        foreach (var tier in Services.ProtoExtract.HatcheryEffectParts.Tiers(stems))
        {
            var parts = Services.ProtoExtract.HatcheryEffectParts.ForTier(stems, tier);
            if (parts.Body is null) continue;
            yield return parts.Body;
            foreach (var f in parts.Floating) yield return f;
        }
    }

    private static System.Text.Json.Nodes.JsonObject ShapeFn(byte[] bin, string needle)
    {
        var r = Services.ProtoExtract.FunctionConstantExtractor.Extract(bin, [needle]);
        return new System.Text.Json.Nodes.JsonObject
        {
            ["ok"] = r.Ok,
            ["function"] = r.FunctionName,
            ["floats"] = new System.Text.Json.Nodes.JsonArray(r.Floats.Select(f => System.Text.Json.Nodes.JsonValue.Create(f)).ToArray()),
            ["calls"] = new System.Text.Json.Nodes.JsonArray(r.Calls.Select(c => System.Text.Json.Nodes.JsonValue.Create(c)).ToArray()),
        };
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
        // Allow catalog pieces + hatchery floating sub-pieces (bolt/probe/rings/tops). The sub-pieces are real
        // device meshes that drive the hatchery effect but are not standalone catalog entries; gate them by the
        // safe naming pattern (no traversal, ei_hatchery_<tier>_<floatingPart>) rather than the catalog allowlist.
        if (!EnvCatalog.IsKnownPiece(stem) && !IsHatcheryFloatingPart(stem))
            return NotFound(new { error = "unknown env mesh" });

        var res = await meshes.GetGlbAsync(stem, device, ct);
        if (!res.Ok) return StatusCode(res.Status, new { error = res.Diagnostics });
        return File(res.Glb!, "model/gltf-binary", $"{stem}.glb");
    }
}
