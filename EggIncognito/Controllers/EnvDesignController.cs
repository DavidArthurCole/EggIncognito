using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Named environment designs for the playground designer, stored in Postgres. Reads are public; save + delete
// need a contributor+ (the shared-store ACL, same authority as StoredEndpointController). Delete also allows
// the owner or an admin. The payload is opaque app JSON (the client owns its shape): validated as well-formed
// + size-capped, never parsed as proto. Without a DB, reads return empty and writes 503.
[ApiController]
[Route("api/env/designs")]
public sealed class EnvDesignController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase
{
    private const int MaxPayloadBytes = 2_000_000; // a design is small JSON; cap protects the column.

    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IActionResult? RequireContributor() =>
        currentUser.IsAtLeast(UserRole.Contributor)
            ? null : StatusCode(403, new { error = "contributor role required" });

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List()
    {
        var db = Db;
        if (db is null) return Ok(new { designs = Array.Empty<object>() });
        var rows = await db.EnvDesigns.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new { d.Name, d.UpdatedAt, owner = d.OwnerUserId })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(new { designs = rows });
    }

    [HttpGet("{name}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Get(string name)
    {
        var db = Db;
        if (db is null) return NotFound(new { error = "no database configured" });
        var row = await db.EnvDesigns.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (row is null) return NotFound(new { error = "unknown design" });
        return Content(row.Payload, "application/json");
    }

    public sealed record SaveDesign(string Payload);

    [HttpPut("{name}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string name, [FromBody] SaveDesign body)
    {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });

        var payload = body?.Payload ?? "";
        if (System.Text.Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
            return BadRequest(new { error = "payload too large" });
        try { using var _ = System.Text.Json.JsonDocument.Parse(payload); }
        catch { return BadRequest(new { error = "payload is not valid JSON" }); }

        var existing = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (existing is null)
        {
            db.EnvDesigns.Add(new EnvDesign { Name = name, Payload = payload, OwnerUserId = currentUser.DiscordId });
        }
        else
        {
            existing.Payload = payload;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { saved = name });
    }

    [HttpDelete("{name}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(string name)
    {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var row = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (row is null) return NotFound(new { error = "unknown design" });
        // owner or admin only (a contributor cannot delete another's design).
        if (row.OwnerUserId != currentUser.DiscordId && !currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "only the owner or an admin can delete this design" });
        db.EnvDesigns.Remove(row);
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { deleted = name });
    }
}
