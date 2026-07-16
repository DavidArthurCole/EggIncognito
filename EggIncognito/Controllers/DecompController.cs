using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;
using EggIncognito.Services.Devices;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Controllers;


[ApiController]
[Route("api/decomp")]
public sealed class DecompController(
    GameBinaryProvider binaries, ICurrentUser currentUser, DeviceCaptureConfig capture,
    IProcessRunner runner, IWebHostEnvironment env) : ControllerBase
{
   
   
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

   
    [HttpGet("function-constants")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> FunctionConstants([FromQuery] string name, [FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });
        return await ExtractAsync([name], device, ct);
    }

   
   
   
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
            ulong textVm = 0, textOff = 0;
            if (MachoText.TryFindText(tgtBytes, out var tfo, out _, out var tvm)) { textVm = tvm; textOff = (ulong)tfo; }

           
            var exact = report.Symbols.Where(s => s.Name == name).ToList();
            if (exact.Count > 0)
                return VaResult(tgtBytes, report, exact[0].Name, exact[0].Value, textVm, textOff, "exact-recovered", null);

           
           
            var embedded = report.Symbols
                .Where(s => s.Name != name && s.Name.Contains(name, StringComparison.Ordinal))
                .OrderBy(s => s.Name.Length).ToList();
            foreach (var lam in embedded)
            {
                var referrers = Arm64AddrRefResolver.FindReferrers(tgtBytes, lam.Value);
                if (referrers.Count > 0)
                    return VaResult(tgtBytes, report, name + " (via referrer of " + lam.Name + ")", referrers[0].FunctionVa,
                        textVm, textOff, "addr-referrer", referrers.Take(5)
                            .Select(r => new { fnVa = "0x" + r.FunctionVa.ToString("x"), r.HitCount }).ToList());
            }

           
            if (embedded.Count > 0)
                return VaResult(tgtBytes, report, embedded[0].Name, embedded[0].Value, textVm, textOff, "contains-fallback", null);

            return Ok(new { ok = false, tier = report.Tier, recovered = report.Recovered, diagnostics = $"{name} not recovered and no referrer found" });
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

   
   
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

   
   
   
   
    [HttpGet("hatchery-assembly")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> HatcheryAssembly([FromQuery] string? device, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        try
        {
            var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

            var asm = EggIncognito.Services.ProtoExtract.Decomp.HatcheryAssemblyRecovery.Recover(bin);
            var main = FunctionConstantExtractor.Extract(bin, ["FarmScene14updateHatcheryEP14GameControllerb"]);

            var json = asm.ToJson();
            json["main"] = new System.Text.Json.Nodes.JsonObject
            {
                ["function"] = main.FunctionName,
                ["floats"] = new System.Text.Json.Nodes.JsonArray(main.Floats.Select(f => System.Text.Json.Nodes.JsonValue.Create(f)).ToArray()),
                ["calls"] = new System.Text.Json.Nodes.JsonArray(main.Calls.Select(c => System.Text.Json.Nodes.JsonValue.Create(c)).ToArray()),
            };

           
           
            EggIncognito.Services.ProtoExtract.FunctionConstantExtractor.ExtractResult Ex(string n) =>
                FunctionConstantExtractor.Extract(bin, [n]);
            System.Text.Json.Nodes.JsonObject Shaped(string label, string needle)
            {
                var r = Ex(needle);
                return new System.Text.Json.Nodes.JsonObject
                {
                    ["label"] = label,
                    ["ok"] = r.Ok,
                    ["function"] = r.FunctionName,
                    ["floats"] = new System.Text.Json.Nodes.JsonArray(r.Floats.Select(f => System.Text.Json.Nodes.JsonValue.Create(f)).ToArray()),
                    ["calls"] = new System.Text.Json.Nodes.JsonArray(r.Calls.Select(c => System.Text.Json.Nodes.JsonValue.Create(c)).ToArray()),
                };
            }
            json["helpers"] = new System.Text.Json.Nodes.JsonArray(
                Shaped("rotate_pyramid", "FarmScene14rotate_pyramidEP14GameControlleri"),
                Shaped("fire_predicate", "updateHatcheryEP14GameControllerbE3$_6FbvEclEv"),
                Shaped("beam_lambda_$_5_body", "updateHatcheryEP14GameControllerbE3$_5FN5Eigen6MatrixIfLi4ELi4ELi0ELi4ELi4EEEvEEclEv"));
            return Content(json.ToJsonString(), "application/json");
        }
        catch (System.DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (System.Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

   
   
   
   
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

   
   
   
   
    [HttpGet("signature")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Signature(
        [FromQuery] string name, [FromQuery] string? refVersion, [FromQuery] int instructions, CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });
        if (instructions <= 0) instructions = 8;
        try
        {
            var (ok, refBytes, _, diag) = await binaries.GetRecoveryInputsAsync(refVersion, "/dev/null", ct);
           
            if (refBytes is null)
            {
                var (rok, rb, rdiag) = await binaries.GetBinaryAsync(null, ct);
                refBytes = rb;
                if (refBytes is null) return Ok(new { ok = false, diagnostics = rdiag ?? diag });
            }

            if (!MachoText.TryFindText(refBytes, out var tfo, out _, out var tvm))
                return Ok(new { ok = false, diagnostics = "reference has no __text" });
            var syms = MachoSymbols.Read(refBytes);
            if (!MachoSymbols.TryFindFunc(syms, [name], out var fn))
                return Ok(new { ok = false, diagnostics = $"symbol not found in reference: {name}" });

            var pat = Arm64Signature.Build(refBytes, fn.Start, fn.End, tvm, tfo, instructions);
            return Ok(new
            {
                ok = pat.Ok,
                name = fn.Name,
                refFunctionVa = "0x" + fn.Start.ToString("x"),
                refFunctionLen = (int)(fn.End - fn.Start),
                instructions = pat.Instructions,
                maskedWords = pat.MaskedWords,
                pattern = pat.FridaPattern,
                diagnostics = pat.Diagnostics,
                hint = "frida: Memory.scan(module.base, module.size, pattern, {onMatch, onComplete}); fewer matches = better. Widen instructions if 0 matches, narrow if many.",
            });
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

   
   
   
    [HttpGet("disasm")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Disasm(
        [FromQuery] string name, [FromQuery] string? device, [FromQuery] string mode = "list",
        [FromQuery] int max = 512, [FromQuery] bool live = false, CancellationToken ct = default)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });
        try
        {
            var (ok, bin, syms, source, diag) = await ResolveBinaryAsync(device, live, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });

            switch (mode.ToLowerInvariant())
            {
                case "constants":
                    var ex = syms is null
                        ? FunctionConstantExtractor.Extract(bin, [name])
                        : FunctionConstantExtractor.ExtractWith(bin, syms, [name]);
                    return Ok(new { ok = ex.Ok, mode, source, result = Shape(ex) });

                case "addresses" or "table":
                    var scan = syms is null
                        ? Arm64DataTableReader.Scan(bin, [name])
                        : Arm64DataTableReader.ScanWith(bin, syms, [name]);
                    return Ok(new
                    {
                        ok = scan.Ok,
                        mode,
                        source,
                        function = scan.FunctionName,
                        diagnostics = scan.Diagnostics,
                        addresses = scan.Addresses.Select(a => new
                        {
                            va = "0x" + a.Va.ToString("x"), a.Segment, a.Section, a.Via,
                        }).ToList(),
                    });

                case "list":
                default:
                    var lst = syms is null
                        ? Arm64DataTableReader.List(bin, [name], Math.Clamp(max, 1, 4096))
                        : Arm64DataTableReader.ListWith(bin, syms, [name], Math.Clamp(max, 1, 4096));
                    return Ok(new
                    {
                        ok = lst.Ok,
                        mode = "list",
                        source,
                        function = lst.FunctionName,
                        start = "0x" + lst.Start.ToString("x"),
                        end = "0x" + lst.End.ToString("x"),
                        diagnostics = lst.Diagnostics,
                        instructions = lst.Instructions.Select(i => new
                        {
                            va = "0x" + i.Va.ToString("x"), i.Mnemonic, i.Operands,
                        }).ToList(),
                    });
            }
        }
        catch (DllNotFoundException) { return Ok(new { ok = false, diagnostics = "arm64 disassembler native lib unavailable" }); }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    [HttpGet("live-pull")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> LivePull(CancellationToken ct)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        var (ok, bytes, syms, grafted, diag) = await binaries.GetLiveBinaryAsync(ct);
        return Ok(new
        {
            ok,
            bytes = bytes?.Length ?? 0,
            symbols = syms?.Count ?? 0,
            grafted,
            diagnostics = diag,
        });
    }

    private async Task<(bool Ok, byte[]? Bin, IReadOnlyList<MachoSymbols.Symbol>? Syms, string Source, string? Diag)>
        ResolveBinaryAsync(string? device, bool live, CancellationToken ct)
    {
        if (live)
        {
            var (lok, lbytes, lsyms, grafted, ldiag) = await binaries.GetLiveBinaryAsync(ct);
            if (lok && lbytes is not null)
                return (true, lbytes, lsyms, grafted ? "device-grafted" : "device", ldiag);
            return (false, null, null, "device", ldiag);
        }
        var (ok, bin, diag) = await binaries.GetBinaryAsync(device, ct);
        return (ok, bin, null, "stash", diag);
    }

    [HttpGet("section")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Section(
        [FromQuery] string va, [FromQuery] int count, [FromQuery] string elem = "f64",
        [FromQuery] string? device = null, [FromQuery] bool live = false, CancellationToken ct = default)
    {
        if (!currentUser.IsAtLeast(EggIncognito.Data.Models.UserRole.Admin))
            return StatusCode(403, new { error = "admin role required" });
        if (!TryParseVa(va, out var addr)) return BadRequest(new { error = "va must be hex (0x...) or decimal" });
        if (!Arm64ConstSectionReader.TryParseElem(elem, out var elemType))
            return BadRequest(new { error = "elem must be one of f32,f64,i32,i64,u32,u64" });
        try
        {
            var (ok, bin, _, source, diag) = await ResolveBinaryAsync(device, live, ct);
            if (!ok || bin is null) return Ok(new { ok = false, diagnostics = diag });
            var dump = Arm64ConstSectionReader.Dump(bin, addr, count, elemType);
            return Ok(new
            {
                ok = dump.Ok,
                source,
                va = "0x" + dump.Va.ToString("x"),
                segment = dump.Segment,
                section = dump.Section,
                elem,
                values = dump.Values,
                diagnostics = dump.Diagnostics,
            });
        }
        catch (Exception ex) { return Ok(new { ok = false, diagnostics = ex.Message }); }
    }

    private static bool TryParseVa(string s, out ulong va)
    {
        va = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(s.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out va);
        return ulong.TryParse(s, out va);
    }

    private IActionResult VaResult(
        byte[] tgt, SymbolRecovery.RecoveryReport report, string name, ulong va, ulong textVm, ulong textOff,
        string method, object? detail)
    {
        bool snapped = MachoFunctionStarts.TryEnclosingStart(tgt, va, out var startVa, out var endVa);
        ulong hookVa = snapped ? startVa : va;
        bool wasMidFunction = snapped && startVa != va;
        return Ok(new
        {
            ok = true,
            tier = report.Tier,
            recovered = report.Recovered,
            method,
            name,
            va = "0x" + va.ToString("x"),
            textVmAddr = "0x" + textVm.ToString("x"),
            textFileOff = "0x" + textOff.ToString("x"),
            rawTextOffset = "0x" + (va - textVm).ToString("x"),
            functionStartVa = "0x" + hookVa.ToString("x"),
            functionEndVa = snapped ? "0x" + endVa.ToString("x") : null,
            hookOffset = "0x" + (hookVa - textVm).ToString("x"),
            wasMidFunction,
            detail,
        });
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
