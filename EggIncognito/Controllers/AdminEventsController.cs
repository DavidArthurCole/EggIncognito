using System.Text.Json;
using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/events")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class AdminEventsController(IServiceProvider services) : ControllerBase {
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
    private GameEventBackfill? Backfill => services.GetService(typeof(GameEventBackfill)) as GameEventBackfill;

    [HttpGet("stats")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Stats(CancellationToken ct) {
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        long total = await db.GameEvents.LongCountAsync(ct);
        long device = await db.GameEvents
            .LongCountAsync(e => e.Source == GameEventSources.Device, ct);
        var latest = await db.GameEvents.AsNoTracking()
            .OrderByDescending(e => e.StartTime)
            .FirstOrDefaultAsync(ct);
        return Ok(new EventStatsResponse(total, device, total - device,
            latest is null ? null : EventsController.ToDto(latest)));
    }

    [HttpPost("sweep-snapshots")]
    public async Task<IActionResult> SweepSnapshots(CancellationToken ct) {
        var backfill = Backfill;
        if (backfill is null) return StatusCode(503, new { error = "no database configured" });
        return Ok(await backfill.SweepSnapshotsAsync(ct));
    }

    [HttpPost("import-carpet")]
    public async Task<IActionResult> ImportCarpet(
        [FromBody] CarpetImportRequest request, CancellationToken ct) {
        var backfill = Backfill;
        if (backfill is null) return StatusCode(503, new { error = "no database configured" });
        if (!string.IsNullOrWhiteSpace(request?.Url)) {
            if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https")) {
                return BadRequest(new { error = "invalid url" });
            }
        }
        try {
            return Ok(await backfill.ImportCarpetAsync(request?.Url, ct));
        } catch (HttpRequestException ex) {
            return BadRequest(new { error = $"fetch failed: {ex.Message}" });
        } catch (JsonException ex) {
            return BadRequest(new { error = $"invalid carpet payload: {ex.Message}" });
        } catch (OperationCanceledException ex) {
            return BadRequest(new { error = $"fetch failed: {ex.Message}" });
        }
    }
}
