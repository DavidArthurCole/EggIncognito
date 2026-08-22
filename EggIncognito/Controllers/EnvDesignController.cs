using System.Data.Common;
using System.Text;
using System.Text.Json;
using EggIdentity.Contract;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.EnvDesign;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/env/designs")]
[ApiAccess(ApiAccessLevel.Contributor)]
public sealed class EnvDesignController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase {
    private const int MaxPayloadBytes = 2_000_000;
    private const int VersionSaveAttempts = 4;
    private const string UniqueViolation = "23505";

    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private ObjectResult? RequireContributor() =>
        currentUser.IsAtLeast(UserRole.Contributor)
            ? null
            : StatusCode(403, new { error = "contributor role required" });

    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> List() {
        if (RequireContributor() is { } no) return no;
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
    public async Task<IActionResult> Get(string name) {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return NotFound(new { error = "no database configured" });
        var row = await db.EnvDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        return row is null ? NotFound(new { error = "unknown design" }) : Content(row.Payload, "application/json");
    }


    [HttpPut("{name}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Save(string name, [FromBody] SaveDesign body) {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name required" });

        string payload = body?.Payload ?? "";
        if (Encoding.UTF8.GetByteCount(payload) > MaxPayloadBytes)
            return BadRequest(new { error = "payload too large" });
        try {
            using var _ = JsonDocument.Parse(payload);
        } catch {
            return BadRequest(new { error = "payload is not valid JSON" });
        }

        var design = await UpsertAsync(db, name, payload);
        int next = await AddVersionAsync(db, new EnvDesignVersion {
            DesignId = design.Id,
            Payload = payload,
            AuthorUserId = currentUser.UserId,
            Note = Trim(body?.Note)
        });
        return Ok(new { saved = name, version = next });
    }


    [HttpGet("{name}/versions")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Versions(string name) {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return Ok(new { versions = Array.Empty<object>() });
        var design = await db.EnvDesigns.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (design is null) return NotFound(new { error = "unknown design" });
        var rows = await db.EnvDesignVersions.AsNoTracking()
            .Where(v => v.DesignId == design.Id)
            .OrderByDescending(v => v.VersionNo)
            .Select(v => new { v.VersionNo, v.Note, v.CreatedAt, v.AuthorUserId, v.RolledBackFrom })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(new { versions = rows });
    }


    [HttpPost("{name}/rollback")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Rollback(string name, [FromBody] RollbackBody body) {
        if (RequireContributor() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var design = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, HttpContext.RequestAborted);
        if (design is null) return NotFound(new { error = "unknown design" });
        var src = await db.EnvDesignVersions
            .FirstOrDefaultAsync(v => v.DesignId == design.Id && v.VersionNo == body.VersionNo,
                HttpContext.RequestAborted);
        if (src is null) return NotFound(new { error = "unknown version" });

        design.Payload = src.Payload;
        design.UpdatedAt = DateTimeOffset.UtcNow;
        int next = await AddVersionAsync(db, new EnvDesignVersion {
            DesignId = design.Id,
            Payload = src.Payload,
            AuthorUserId = currentUser.UserId,
            RolledBackFrom = src.VersionNo,
            Note = $"rolled back to v{src.VersionNo}"
        });
        return Ok(new { rolledBack = name, fromVersion = src.VersionNo, newVersion = next });
    }

    private async Task<EnvDesign> UpsertAsync(EggIncognitoDbContext db, string name, string payload) {
        var ct = HttpContext.RequestAborted;
        var existing = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, ct);
        if (existing is not null) {
            existing.Payload = payload;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            return existing;
        }

        var created = new EnvDesign { Name = name, Payload = payload, OwnerUserId = currentUser.UserId };
        db.EnvDesigns.Add(created);
        try {
            await db.SaveChangesAsync(ct);
            return created;
        } catch (DbUpdateException ex) when (IsUniqueViolation(ex)) {
            db.Entry(created).State = EntityState.Detached;
            return await db.EnvDesigns.FirstAsync(d => d.Name == name, ct);
        }
    }

    private async Task<int> AddVersionAsync(EggIncognitoDbContext db, EnvDesignVersion version) {
        var ct = HttpContext.RequestAborted;
        int attempt = 0;
        while (true) {
            attempt++;
            version.VersionNo = await NextVersionNo(db, version.DesignId, ct);
            db.EnvDesignVersions.Add(version);
            try {
                await db.SaveChangesAsync(ct);
                return version.VersionNo;
            } catch (DbUpdateException ex) when (attempt < VersionSaveAttempts && IsUniqueViolation(ex)) {
                db.Entry(version).State = EntityState.Detached;
            }
        }
    }

    private static async Task<int> NextVersionNo(EggIncognitoDbContext db, long designId, CancellationToken ct) {
        int max = await db.EnvDesignVersions.AsNoTracking()
            .Where(v => v.DesignId == designId)
            .MaxAsync(v => (int?)v.VersionNo, ct) ?? 0;
        return max + 1;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: UniqueViolation };

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [HttpDelete("{name}")]
    [EnableRateLimiting("write")]
    public async Task<IActionResult> Delete(string name) {
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
