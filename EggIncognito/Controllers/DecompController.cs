using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Services;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;

// Admin decomp-extraction endpoints. Reads float/double constants + call targets out of named functions in the
// egginc binary (pulled from the device, cached). The reusable primitive for extracting game behavior instead
// of hand-authoring it (see CLAUDE.md "EXTRACT, don't author"). Admin-only; degrades with a diagnostic when no
// binary or the disassembler is unavailable. Never throws to the client.
[ApiController]
[Route("api/decomp")]
public sealed class DecompController(GameBinaryProvider binaries, ICurrentUser currentUser) : ControllerBase
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

    // The Chicken Universe hab floating effect = a GalaxyParticle system. The orbit math is NOT in onBirth's
    // body (that just installs lambdas); it lives in the four onBirth lambda operator() bodies: $_0 builds the
    // per-frame Matrix4f transform (the orbit), $_1 + $_3 the Vector3f axes/offsets, $_2 a float scalar
    // (speed/phase). Each constant loads as a q-vector (Eigen) the disassembler now reads. Returns all four
    // lambdas plus the outer onBirth/update for context.
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

            object Lambda(string tag) =>
                Shape(FunctionConstantExtractor.Extract(bin, [$"GalaxyParticle7onBirthEP14ParticleSystemE3$_{tag}", "clEv"]));

            return Ok(new
            {
                ok = true,
                transform = Lambda("0"),  // Matrix4f orbit transform
                axisA = Lambda("1"),      // Vector3f
                scalar = Lambda("2"),     // float
                axisB = Lambda("3"),      // Vector3f
                onBirth = Shape(FunctionConstantExtractor.Extract(bin, ["GalaxyParticle7onBirthEP14ParticleSystem"])),
                update = Shape(FunctionConstantExtractor.Extract(bin, ["GalaxyParticle6updateEP14ParticleSystemf"])),
            });
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
