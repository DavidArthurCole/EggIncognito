using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Admin decomp-extraction endpoints. Reads float/double constants + call targets out of named functions in the
// egginc binary (pulled from the device, cached). The reusable primitive for extracting game behavior instead
// of hand-authoring it (see CLAUDE.md "EXTRACT, don't author"). Admin-only; degrades with a diagnostic when no
// binary or the disassembler is unavailable. Never throws to the client.
[ApiController]
[Route("api/decomp")]
public sealed class DecompController(
    GameBinaryProvider binaries, ICurrentUser currentUser, DeviceCaptureConfig capture,
    IProcessRunner runner, IWebHostEnvironment env) : ControllerBase
{
    // Diagnostic: how many symbols the parser sees + sample names matching an optional filter. Tells us whether
    // the live binary is symbolized + what the real mangled names look like (so needles can be tuned).
    [HttpGet("symbols")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Symbols([FromQuery] string? filter, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
        if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

        var syms = MachoSymbols.Read(bin);
        var named = syms.Where(s => !string.IsNullOrEmpty(s.Name)).ToList();
        var matches = string.IsNullOrEmpty(filter)
            ? named.Take(40).Select(s => s.Name)
            : named.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(60).Select(s => s.Name);
        return Ok(new
        {
            ok = true,
            totalSymbols = syms.Count,
            namedSymbols = named.Count,
            withAddress = named.Count(s => s.Value != 0),
            sample = matches.ToList(),
        });
    }

    // Constants + calls for the first function whose symbol contains `name`. The generic extraction primitive.
    [HttpGet("function-constants")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> FunctionConstants([FromQuery] string name, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });
        return await ExtractAsync([name], device, ct);
    }

    // The Chicken Universe hab floating effect = a GalaxyParticle system. The per-particle motion (count +
    // orbit/transform) lives in DrawableGalaxyParticle::update; the spawn placement in GalaxyParticle::onBirth.
    // These two carry the substantive constants (the system-level GalaxyParticle::update is a thin dispatcher).
    // Needles are exact-mangled so DrawableGalaxyParticle does not shadow GalaxyParticle and vice versa.
    [HttpGet("galaxy-particle")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> GalaxyParticle([FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        try
        {
            var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

            var motion = FunctionConstantExtractor.Extract(bin, ["DrawableGalaxyParticle6updateEf"]);
            var spawn = FunctionConstantExtractor.Extract(bin, ["GalaxyParticle7onBirthEP14ParticleSystem"]);
            return Ok(new { ok = true, motion = Shape(motion), spawn = Shape(spawn) });
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    // v2 cross/adjacent-version symbol recovery: project a symbolized reference's symbols onto a stripped target
    // (LC_FUNCTION_STARTS-anchored, byte-verified), then extract the requested functions' constants from the
    // STRIPPED target using the recovered VAs. For the device path when only an adjacent symbolized build exists.
    [HttpGet("recover")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Recover(
        [FromQuery] string? name, [FromQuery] string? refVersion, [FromQuery] string? targetPath, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        try
        {
            var (ok, refBytes, tgtBytes, diag) = await binaries.GetRecoveryInputsAsync(refVersion, targetPath, ct);
            if (!ok || refBytes is null || tgtBytes is null) return Ok(new { ok = false, diagnostics = diag });

            var needles = string.IsNullOrWhiteSpace(name)
                ? new[] { "GalaxyParticle6update", "GalaxyParticle7onBirth", "FarmScene10updateSilo" }
                : [name];
            var report = SymbolRecovery.Recover(refBytes, tgtBytes, needles);

            var extracted = needles.Select(n =>
            {
                var ex = FunctionConstantExtractor.ExtractWith(tgtBytes, report.Symbols, [n]);
                return new { needle = n, result = Shape(ex) };
            }).ToList();

            return Ok(new
            {
                ok = true,
                tier = report.Tier,
                recovered = report.Recovered,
                requestedFound = report.RequestedFound,
                requestedMissing = report.RequestedMissing,
                diagnostics = report.Diagnostics,
                extracted,
            });
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    // Resolve a symbol's recovered VA on the stripped device target, for hooking it live with frida. Recovers the
    // adjacent symbolized reference's symbols onto the target, then EXACT-matches the requested name (so a lambda
    // closure whose mangled name embeds the real signature does not shadow the bare function). Returns the VA + the
    // target's __text vmaddr/fileoff so the caller computes the runtime address: module.base + (VA - textVm).
    // Admin-gated. The path for the data-driven effects that need a live hook (the universe hatchery sparkle).
    [HttpGet("resolve-va")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ResolveVa(
        [FromQuery] string name, [FromQuery] string? refVersion, [FromQuery] string? targetPath, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });
        try
        {
            var (ok, refBytes, tgtBytes, diag) = await binaries.GetRecoveryInputsAsync(refVersion, targetPath, ct);
            if (!ok || refBytes is null || tgtBytes is null) return Ok(new { ok = false, diagnostics = diag });

            var report = SymbolRecovery.Recover(refBytes, tgtBytes, [name]);
            // exact name first; fall back to the shortest Contains match (the bare function, not a lambda wrapper).
            var exact = report.Symbols.Where(s => s.Name == name).ToList();
            var candidates = exact.Count > 0
                ? exact
                : report.Symbols.Where(s => s.Name.Contains(name, StringComparison.Ordinal))
                    .OrderBy(s => s.Name.Length).ToList();
            if (candidates.Count == 0)
                return Ok(new { ok = false, tier = report.Tier, recovered = report.Recovered, diagnostics = $"{name} not in recovered set" });

            ulong textVm = 0, textOff = 0;
            if (MachoText.TryFindText(tgtBytes, out var tfo, out _, out var tvm)) { textVm = tvm; textOff = (ulong)tfo; }

            var pick = candidates[0];
            return Ok(new
            {
                ok = true,
                tier = report.Tier,
                recovered = report.Recovered,
                exactMatch = exact.Count > 0,
                name = pick.Name,
                va = "0x" + pick.Value.ToString("x"),
                textVmAddr = "0x" + textVm.ToString("x"),
                textFileOff = "0x" + textOff.ToString("x"),
                // frida runtime address = module.base + (va - textVmAddr). The offset-from-text-base, precomputed:
                textOffset = "0x" + (pick.Value - textVm).ToString("x"),
                allCandidates = candidates.Take(8).Select(s => new { s.Name, va = "0x" + s.Value.ToString("x") }).ToList(),
            });
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    // Effect recovery framework: symbolically execute the effect's per-particle update and return the per-frame
    // placement math as an expression tree the renderer replays (not just constants). Admin-gated; the model's
    // opaqueCount is the honesty signal. Sources the symbolized binary like the other decomp endpoints.
    [HttpGet("effect")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Effect([FromQuery] string? name, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (!string.Equals(name, "galaxy-particle", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "unknown effect; supported: galaxy-particle" });
        try
        {
            var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var model = EggIncognito.Services.ProtoExtract.Decomp.EffectRecovery.Recover(
                bin, "DrawableGalaxyParticle6updateEf", "GalaxyParticle7onBirthEP14ParticleSystem",
                new EggIncognito.Services.ProtoExtract.Decomp.Const(27));
            return Content(model.ToJson().ToJsonString(), "application/json");
        }
        catch (System.DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    // Recovered farm singleton placement: the missionControl/fuelTank/hoa position formulas extracted from
    // FarmScene::*Pos as expression trees over Input("farmWidth"). The dynamic, farm-size-dependent offset the
    // game computes at runtime, now read from the binary instead of hand-authored. Admin-gated.
    [HttpGet("farm-placement")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> FarmPlacement([FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        try
        {
            var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var R = EggIncognito.Services.ProtoExtract.Decomp.FarmPlacementRecovery.Recover;
            var json = new System.Text.Json.Nodes.JsonObject
            {
                ["ok"] = true,
                ["missionControl"] = R(bin, "FarmScene17missionControlPos").ToJson(),
                ["fuelTank"] = R(bin, "FarmScene11fuelTankPos").ToJson(),
                ["hoa"] = R(bin, "FarmScene6hoaPos").ToJson(),
            };
            return Content(json.ToJsonString(), "application/json");
        }
        catch (System.DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    // Dynamic building-effect discovery: returns every effect the resolver finds for a building mesh stem,
    // extracted from the binary call graph (no hardcoded per-building list). Each effect = a recovered EffectModel
    // the renderer drives. Empty array = no effects discovered (or no binary). Public-ish (read-gated like the
    // env layout); the heavy lifting is cached binary analysis.
    [HttpGet("building-effects")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> BuildingEffects([FromQuery] string stem, [FromQuery] string? device, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stem)) return BadRequest(new { error = "stem required" });
        try
        {
            var (ok, bin, _) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { stem, effects = Array.Empty<object>() });
            var models = EggIncognito.Services.ProtoExtract.Decomp.BuildingEffectResolver.Resolve(bin, stem);
            var arr = new System.Text.Json.Nodes.JsonArray(models.Select(m => (System.Text.Json.Nodes.JsonNode)m.ToJson()).ToArray());
            return Content(new System.Text.Json.Nodes.JsonObject { ["stem"] = stem, ["effects"] = arr }.ToJsonString(), "application/json");
        }
        catch (System.DllNotFoundException) { return Ok(new { stem, effects = Array.Empty<object>() }); }
        catch (Exception ex) { return Ok(new { stem, effects = Array.Empty<object>(), diagnostics = ex.Message }); }
    }

    // Live particle capture: hook ParticleBatchedMesh::addParticle on the running game via frida, log every
    // particle's per-frame world transform, cluster by mesh pointer to isolate one effect. The path for the
    // data-driven effects that are NOT statically extractable (the universe hatchery sparkle). Admin-gated; needs
    // the iOS ssh creds (DeviceCapture:Ios / DeviceUpdate:Ios) + frida-server live on the phone. The caller
    // triggers this while the target farm is on screen. Returns the clustered capture model; degrades cleanly.
    [HttpPost("particle-capture")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ParticleCapture([FromQuery] string? addrOffset, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });

        var host = capture.IosSshHost;
        var key = capture.IosSshKeyPath;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key))
            return StatusCode(503, new { ok = false, diagnostics = "ios ssh not configured (DeviceCapture:Ios:SshHost + SshKeyPath)" });

        var script = Path.Combine(env.ContentRootPath, "..", "tools", "ios-frida", "particle-capture.js");
        if (!System.IO.File.Exists(script))
            script = Path.Combine(env.ContentRootPath, "tools", "ios-frida", "particle-capture.js");
        if (!System.IO.File.Exists(script))
            return StatusCode(500, new { ok = false, diagnostics = "particle-capture.js not found under tools/ios-frida" });

        try
        {
            var capturer = new IosParticleCapturer(runner, host, capture.IosSshPort, key, script, addrOffset);
            var model = await capturer.CaptureAsync(ct);
            if (model is null) return Ok(new { ok = false, diagnostics = "capture failed (scp/frida)" });
            return Content(model.Value.ToJson().ToJsonString(), "application/json");
        }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    private async Task<IActionResult> ExtractAsync(string[] needles, string? device, CancellationToken ct)
    {
        try
        {
            var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var res = FunctionConstantExtractor.Extract(bin, needles);
            return Ok(new { ok = res.Ok, result = Shape(res) });
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    private static object Shape(FunctionConstantExtractor.ExtractResult r) => new
    {
        ok = r.Ok, function = r.FunctionName, floats = r.Floats, calls = r.Calls, diagnostics = r.Diagnostics,
    };
}
