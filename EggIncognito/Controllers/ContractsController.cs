using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/contracts")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class ContractsController(IServiceProvider services) : ControllerBase {
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
    private ContractPredictor? Predictor => services.GetService(typeof(ContractPredictor)) as ContractPredictor;

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List(
        [FromQuery] double? after,
        [FromQuery] double? before,
        [FromQuery] bool? leggacy,
        [FromQuery] bool? ultra,
        [FromQuery] string? search,
        [FromQuery] int limit = 500,
        CancellationToken ct = default) {
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (after is { } a && !UnixSeconds.IsValid(a)) return BadRequest(new { error = "after is out of range" });
        if (before is { } b && !UnixSeconds.IsValid(b)) return BadRequest(new { error = "before is out of range" });

        limit = Math.Clamp(limit, 1, 1000);
        var q = db.ContractReleases.AsNoTracking();
        if (after is { } a2) q = q.Where(r => r.EndTime >= UnixSeconds.ToTime(a2));
        if (before is { } b2) q = q.Where(r => r.StartTime <= UnixSeconds.ToTime(b2));
        if (leggacy is { } l) q = q.Where(r => r.Leggacy == l);
        if (ultra is { } u) q = q.Where(r => r.UltraOnly == u);
        if (!string.IsNullOrWhiteSpace(search)) {
            var pattern = $"%{search}%";
            q = q.Where(r => EF.Functions.ILike(r.ContractId, pattern) || EF.Functions.ILike(r.Name, pattern));
        }
        int total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.StartTime).ThenByDescending(r => r.Id).Take(limit).ToListAsync(ct);
        return Ok(new ContractReleaseListResponse(total, rows.Select(ToDto).ToList()));
    }

    [HttpGet("predictions")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Predictions(
        [FromQuery] string? contract,
        [FromQuery] int horizon = 9,
        CancellationToken ct = default) {
        var predictor = Predictor;
        if (predictor is null) return StatusCode(503, new { error = "no database configured" });

        if (!string.IsNullOrWhiteSpace(contract)) {
            var estimate = await predictor.GetContractAsync(contract.Trim(), ct);
            if (estimate is null) return NotFound(new { error = "unknown contract" });
            return Ok(estimate);
        }
        return Ok(await predictor.GetSlotsAsync(Math.Clamp(horizon, 1, 30), ct));
    }

    internal static ContractReleaseDto ToDto(ContractRelease r) => new(
        r.Id, r.ContractId, r.Name, r.Egg, r.CustomEggId, r.SeasonId,
        UnixSeconds.FromTime(r.StartTime), UnixSeconds.FromTime(r.EndTime), r.LengthSeconds,
        r.Leggacy, r.UltraOnly, r.ProphecyEggs, r.CoopAllowed, r.MaxCoopSize, r.Source);
}
