using System.Text.Json.Nodes;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Services.ProtoExtract.Decomp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIdentity.Contract;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/env")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class EnvController(DeviceMeshProvider meshes, ICurrentUser currentUser, GameBinaryProvider binaries)
    : ControllerBase {
    [HttpGet("catalog")]
    public IActionResult Catalog() => Ok(new {
        pieces = EnvCatalog.Pieces.Select(p => new { p.Stem, p.Label, p.Group, p.Singleton, p.Family }),
        habs = EnvCatalog.Habs.Select(p => new { p.Stem, p.Label })
    });


    [HttpGet("family/{stem}")]
    public IActionResult Family(string stem) => Ok(new {
        family = EnvCatalog.Family(stem).Select(p => new { p.Stem, p.Label })
    });


    [HttpGet("farm-layout")]
    public async Task<IActionResult> FarmLayout([FromQuery] string? hab = null, [FromQuery] string? device = null,
        CancellationToken ct = default) {
        string stem = hab is not null && EnvCatalog.IsKnownPiece(hab)
            ? hab
            : Services.ProtoExtract.FarmLayout.DefaultHabPlaceholder;
        var layout = await RecoveredOrFallbackLayout(stem, device, ct);
        var placed = layout
            .Where(p => EnvCatalog.IsKnownPiece(p.Stem))
            .Select(p => new { p.Stem, label = LabelFor(p.Stem), p.Pos, p.RotY, p.Scale, p.Recenter });
        return Ok(new { elements = placed });
    }


    private async Task<IReadOnlyList<FarmLayout.Placed>> RecoveredOrFallbackLayout(
        string stem, string? device, CancellationToken ct) {
        try {
            (bool ok, byte[]? bin, _) = await binaries.GetBinaryAsync(device, ct);
            return !ok || bin is null
                ? Services.ProtoExtract.FarmLayout.Standard(stem)
                : Services.ProtoExtract.FarmLayout.StandardRecovered(stem);
        } catch {
            return Services.ProtoExtract.FarmLayout.Standard(stem);
        }
    }

    private static string LabelFor(string stem) =>
        EnvCatalog.Pieces.FirstOrDefault(p => p.Stem == stem)?.Label ?? stem;


    private static bool IsHatcheryFloatingPart(string stem) {
        if (string.IsNullOrEmpty(stem) || stem.Length > 64) return false;
        foreach (char c in stem) {
            if (!(char.IsLetterOrDigit(c) || c == '_'))
                return false;
        }

        if (!stem.StartsWith("ei_hatchery_", StringComparison.Ordinal)) return false;
        string? tier = HatcheryEffectParts.TierOf(stem);
        if (tier is null) return false;

        string body = "ei_hatchery_" + tier;
        return stem.Length > body.Length + 1 && stem.StartsWith(body + "_", StringComparison.Ordinal);
    }


    [HttpGet("device-stems")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> DeviceStems([FromQuery] string? device, [FromQuery] string? filter,
        CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        (bool ok, var stems, string? diag) = await meshes.ListStemsAsync(device, ct);
        if (!ok) return Ok(new { ok = false, diagnostics = diag });
        var filtered = string.IsNullOrEmpty(filter)
            ? stems
            : stems.Where(s => s.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        return Ok(new { ok = true, count = filtered.Count, stems = filtered });
    }


    [HttpGet("hatchery-effects")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> HatcheryEffects([FromQuery] string? tier, [FromQuery] string? device,
        CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        (bool ok, var stems, string? diag) = await meshes.ListStemsAsync(device, ct);
        if (!ok) return Ok(new { ok = false, diagnostics = diag });

        var tiers = string.IsNullOrWhiteSpace(tier)
            ? HatcheryEffectParts.Tiers(stems)
            : [tier];
        var effects = tiers
            .Select(t => HatcheryEffectParts.ForTier(stems, t))
            .Where(p => p.Body is not null)
            .Select(p => new { tier = p.Tier, body = p.Body, floating = p.Floating })
            .ToList();
        return Ok(new { ok = true, count = effects.Count, effects });
    }


    [HttpGet("hatchery-dump")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> HatcheryDump([FromQuery] string? device, CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });


        (bool sok, var stems, var stats, string? sdiag) =
            await meshes.ListStemsWithStatsAsync(device, HatcheryPieceStems, ct);
        var tiersJson = new JsonArray();
        if (sok) {
            foreach (string tier in HatcheryEffectParts.Tiers(stems)) {
                var parts = HatcheryEffectParts.ForTier(stems, tier);
                if (parts.Body is null) continue;
                var pieces = new JsonArray();
                foreach (string? stem in new[] { parts.Body }.Concat(parts.Floating)) {
                    var st = stats.TryGetValue(stem, out var s) && s.Ok ? s : null;
                    pieces.Add(new JsonObject {
                        ["stem"] = stem,
                        ["floating"] = stem != parts.Body,
                        ["vertexCount"] = st?.VertexCount ?? 0,
                        ["bounds"] = st?.Bounds is { } b
                            ? new JsonObject {
                                ["min"] = new JsonArray(b.Min.X, b.Min.Y, b.Min.Z),
                                ["max"] = new JsonArray(b.Max.X, b.Max.Y, b.Max.Z)
                            }
                            : null
                    });
                }

                tiersJson.Add(new JsonObject {
                    ["tier"] = tier,
                    ["body"] = parts.Body,
                    ["floating"] = new JsonArray(parts.Floating.Select(f => (JsonNode)f).ToArray()),
                    ["pieces"] = pieces
                });
            }
        }

        JsonNode? assembly;
        try {
            (bool bok, byte[]? bin, string? bdiag) = await binaries.GetBinaryAsync(device, ct);
            if (bok && bin is not null) {
                var asm = HatcheryAssemblyRecovery.Recover(bin);
                assembly = asm.ToJson();
                ((JsonObject)assembly)["rotate_pyramid"] = ShapeFn(bin, "FarmScene14rotate_pyramidEP14GameControlleri");
            } else {
                assembly = new JsonObject { ["ok"] = false, ["diagnostics"] = bdiag };
            }
        } catch (Exception ex) {
            assembly = new JsonObject { ["ok"] = false, ["diagnostics"] = ex.Message };
        }

        return Content(new JsonObject {
            ["ok"] = true,
            ["stemsDiagnostics"] = sok ? "ok" : sdiag,
            ["tiers"] = tiersJson,
            ["assembly"] = assembly
        }.ToJsonString(), "application/json");
    }


    private static IEnumerable<string> HatcheryPieceStems(IReadOnlyList<string> stems) {
        foreach (string tier in HatcheryEffectParts.Tiers(stems)) {
            var parts = HatcheryEffectParts.ForTier(stems, tier);
            if (parts.Body is null) continue;
            yield return parts.Body;
            foreach (string f in parts.Floating) yield return f;
        }
    }

    private static JsonObject ShapeFn(byte[] bin, string needle) {
        var r = FunctionConstantExtractor.Extract(bin, [needle]);
        return new JsonObject {
            ["ok"] = r.Ok,
            ["function"] = r.FunctionName,
            ["floats"] = new JsonArray(r.Floats.Select(f => JsonValue.Create(f)).ToArray()),
            ["calls"] = new JsonArray(r.Calls.Select(c => JsonValue.Create(c)).ToArray())
        };
    }


    [HttpGet("{stem}/decode-stats")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> DecodeStats(string stem, [FromQuery] string? device, CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        (bool ok, var stats, string? diag) = await meshes.GetDecodeStatsAsync(stem, device, ct);
        if (!ok || stats is null) return Ok(new { ok = false, diagnostics = diag });
        return Ok(new {
            ok = stats.Ok,
            stem,
            vertexCount = stats.VertexCount,
            indexCount = stats.IndexCount,
            trailingBytes = stats.TrailingBytes,
            multiMesh = stats.TrailingBytes > 0,
            bounds = stats.Bounds is null
                ? null
                : new {
                    min = new[] { stats.Bounds.Min.X, stats.Bounds.Min.Y, stats.Bounds.Min.Z },
                    max = new[] { stats.Bounds.Max.X, stats.Bounds.Max.Y, stats.Bounds.Max.Z }
                },
            diagnostics = stats.Diagnostics
        });
    }


    [HttpGet("{stem}/glb")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Glb(string stem, [FromQuery] string? device, CancellationToken ct) {
        if (!currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });


        if (!EnvCatalog.IsKnownPiece(stem) && !IsHatcheryFloatingPart(stem))
            return NotFound(new { error = "unknown env mesh" });

        var res = await meshes.GetGlbAsync(stem, device, ct);
        return !res.Ok
            ? StatusCode(res.Status, new { error = res.Diagnostics })
            : File(res.Glb!, "model/gltf-binary", $"{stem}.glb");
    }
}
