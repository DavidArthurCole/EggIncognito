using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;

namespace EggIncognito.Controllers;


[ApiController]
[Route("api/env/designs")]
[EggIncognito.Services.Auth.ApiAccess(EggIncognito.Services.Auth.ApiAccessLevel.Public)]
public sealed class EnvDesignController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase
{
    private const int MaxPayloadBytes = 2_000_000;

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

    public sealed record SaveDesign(string Payload, string? Note);

   
   
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
            existing = new EnvDesign { Name = name, Payload = payload, OwnerUserId = currentUser.UserId };
            db.EnvDesigns.Add(existing);
            await db.SaveChangesAsync(HttpContext.RequestAborted);
        }
        else
        {
            existing.Payload = payload;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        var next = await NextVersionNo(db, existing.Id);
        db.EnvDesignVersions.Add(new EnvDesignVersion
        {
            DesignId = existing.Id, VersionNo = next, Payload = payload,
            AuthorUserId = currentUser.UserId, Note = Trim(body?.Note),
        });
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { saved = name, version = next });
    }

   
    [HttpGet("{name}/versions")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Versions(string name)
    {
        var db = Db;
        if (db is null) return Ok(new { versions = Array.Empty<object>() });
        var design = await db.EnvDesigns.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (design is null) return NotFound(new { error = "unknown design" });
        var rows = await db.EnvDesignVersions.AsNoTracking()
            .Where(v => v.DesignId == design.Id)
            .OrderByDescending(v => v.VersionNo)
            .Select(v => new { v.VersionNo, v.Note, v.CreatedAt, v.AuthorUserId, v.RolledBackFrom })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(new { versions = rows });
    }

   
    [HttpGet("{name}/versions/{versionNo:int}")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> GetVersion(string name, int versionNo)
    {
        var db = Db;
        if (db is null) return NotFound(new { error = "no database configured" });
        var design = await db.EnvDesigns.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (design is null) return NotFound(new { error = "unknown design" });
        var row = await db.EnvDesignVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.DesignId == design.Id && v.VersionNo == versionNo, HttpContext.RequestAborted);
        if (row is null) return NotFound(new { error = "unknown version" });
        return Content(row.Payload, "application/json");
    }

    public sealed record RollbackBody(int VersionNo);

   
   
    [HttpPost("{name}/rollback")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Rollback(string name, [FromBody] RollbackBody body)
    {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var design = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (design is null) return NotFound(new { error = "unknown design" });
        var src = await db.EnvDesignVersions
            .FirstOrDefaultAsync(v => v.DesignId == design.Id && v.VersionNo == body.VersionNo, HttpContext.RequestAborted);
        if (src is null) return NotFound(new { error = "unknown version" });

        design.Payload = src.Payload;
        design.UpdatedAt = DateTimeOffset.UtcNow;
        var next = await NextVersionNo(db, design.Id);
        db.EnvDesignVersions.Add(new EnvDesignVersion
        {
            DesignId = design.Id, VersionNo = next, Payload = src.Payload,
            AuthorUserId = currentUser.UserId, RolledBackFrom = src.VersionNo,
            Note = $"rolled back to v{src.VersionNo}",
        });
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { rolledBack = name, fromVersion = src.VersionNo, newVersion = next });
    }

    private static async Task<int> NextVersionNo(EggIncognitoDbContext db, long designId)
    {
        var max = await db.EnvDesignVersions.Where(v => v.DesignId == designId)
            .MaxAsync(v => (int?)v.VersionNo, default) ?? 0;
        return max + 1;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [HttpDelete("{name}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(string name)
    {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var row = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (row is null) return NotFound(new { error = "unknown design" });
        if (row.OwnerUserId != currentUser.UserId && !currentUser.IsAtLeast(UserRole.Admin))
            return StatusCode(403, new { error = "only the owner or an admin can delete this design" });
        db.EnvDesigns.Remove(row);
        await db.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(new { deleted = name });
    }
}
