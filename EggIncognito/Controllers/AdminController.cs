using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

// Admin-only management API. Every action requires the admin role; the role check (and the self-
// lockout guard) run BEFORE the DB resolve, so a non-admin 403s and a self-demote 400s regardless of
// DB state. When no DB is configured, DB-touching actions return 503.
[ApiController]
[Route("api/admin")]
[EnableRateLimiting("write")]
public sealed class AdminController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase
{
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IActionResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    public sealed record SetRole(string Role);

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var rows = await db.Users.AsNoTracking()
            .Select(u => new { u.DiscordId, u.Username, u.Role, u.LastLoginAt }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("users/{discordId}/role")]
    public async Task<IActionResult> SetUserRole(string discordId, [FromBody] SetRole body)
    {
        if (RequireAdmin() is { } no) return no;
        // Self-lockout guard runs before the DB resolve so it is testable without a live DB.
        if (discordId == currentUser.DiscordId && UserRoles.Parse(body.Role) != UserRole.Admin)
            return BadRequest(new { error = "cannot remove your own admin role" });

        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var user = await db.Users.FirstOrDefaultAsync(u => u.DiscordId == discordId);
        if (user is null) return NotFound(new { error = "user not found" });
        user.Role = UserRoles.ToName(UserRoles.Parse(body.Role)); // normalize/validate
        await db.SaveChangesAsync();
        return Ok(new { discordId, role = user.Role });
    }

    [HttpDelete("endpoint/{id:long}")]
    public async Task<IActionResult> DeleteEndpoint(long id)
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var row = await db.StoredEndpoints.FindAsync(id);
        if (row is null) return NotFound();
        db.StoredEndpoints.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    [HttpDelete("route/{id:long}")]
    public async Task<IActionResult> DeleteRoute(long id)
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var row = await db.StoredRoutes.FindAsync(id);
        if (row is null) return NotFound();
        if (row.Source != "db") return BadRequest(new { error = "cannot delete a yaml-sourced route" });
        db.StoredRoutes.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    public sealed record AddTag(string Slug, string Label, string? Color);

    // Tag DEFINITIONS are catalog-level data, so creating/removing them is admin-only (contributors
    // only assign existing tags to subjects, via DocsController). Slug is normalized + must be unique.
    [HttpPost("tag")]
    public async Task<IActionResult> AddTagAsync([FromBody] AddTag body)
    {
        if (RequireAdmin() is { } no) return no;
        var slug = (body.Slug ?? "").Trim().ToLowerInvariant();
        var label = (body.Label ?? "").Trim();
        if (slug.Length == 0 || label.Length == 0) return BadRequest(new { error = "slug and label are required" });

        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (await db.Tags.AnyAsync(t => t.Slug == slug)) return Conflict(new { error = $"tag {slug} already exists" });

        var tag = new Tag { Slug = slug, Label = label, Color = string.IsNullOrWhiteSpace(body.Color) ? null : body.Color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return Ok(new { tag.Id, tag.Slug, tag.Label, tag.Color });
    }

    // Deleting a tag definition also clears its subject_tags join rows (no hard FK, so do it in app).
    [HttpDelete("tag/{id:long}")]
    public async Task<IActionResult> DeleteTag(long id)
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var tag = await db.Tags.FindAsync(id);
        if (tag is null) return NotFound();
        var joins = await db.SubjectTags.Where(s => s.TagId == id).ToListAsync();
        db.SubjectTags.RemoveRange(joins);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }
}
