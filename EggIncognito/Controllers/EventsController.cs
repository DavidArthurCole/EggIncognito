using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/events")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class EventsController(IServiceProvider services) : ControllerBase {
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
    private EventPredictor? Predictor => services.GetService(typeof(EventPredictor)) as EventPredictor;

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List(
        [FromQuery] string? types,
        [FromQuery] bool? ultra,
        [FromQuery] double? after,
        [FromQuery] double? before,
        [FromQuery] double? activeAt,
        [FromQuery] bool active = false,
        [FromQuery] string? source = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default) {
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (after is { } a && !UnixSeconds.IsValid(a)) return BadRequest(new { error = "after is out of range" });
        if (before is { } b && !UnixSeconds.IsValid(b)) return BadRequest(new { error = "before is out of range" });
        if (activeAt is { } at && !UnixSeconds.IsValid(at))
            return BadRequest(new { error = "activeAt is out of range" });

        limit = Math.Clamp(limit, 1, 1000);
        offset = Math.Max(offset, 0);
        var q = db.GameEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(types)) {
            var wanted = types
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            q = q.Where(e => wanted.Contains(e.EventType));
        }
        if (ultra is { } u) q = q.Where(e => e.Ultra == u);
        if (after is { } a2) q = q.Where(e => e.StartTime >= UnixSeconds.ToTime(a2));
        if (before is { } b2) q = q.Where(e => e.StartTime <= UnixSeconds.ToTime(b2));
        double? instant = activeAt ?? (active ? UnixSeconds.FromTime(DateTimeOffset.UtcNow) : null);
        if (instant is { } inst) {
            var t = UnixSeconds.ToTime(inst);
            q = q.Where(e => e.StartTime <= t && e.EndTime >= t);
        }
        if (!string.IsNullOrWhiteSpace(source)) q = q.Where(e => e.Source == source);
        int total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.StartTime).ThenByDescending(e => e.Id)
            .Skip(offset).Take(limit).ToListAsync(ct);
        return Ok(new GameEventListResponse(total, rows.Select(ToDto).ToList()));
    }

    [HttpGet("predictions")]
    [EnableRateLimiting("read")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Predictions(CancellationToken ct) {
        var predictor = Predictor;
        if (predictor is null) return StatusCode(503, new { error = "no database configured" });
        return Ok(await predictor.GetAsync(ct));
    }

    internal static GameEventDto ToDto(GameEvent e) => new(
        e.EventId, e.EventType, e.Message, e.Multiplier, e.Ultra,
        UnixSeconds.FromTime(e.StartTime), UnixSeconds.FromTime(e.EndTime), e.Source);
}
