using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using EggIncognito.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SyncKit.Identity.Client;

namespace EggIncognito.Controllers;

// Admin-only management API. User list/role-mutation goes through SyncKit.Identity;
// everything else is local Postgres. DB-touching actions return 503 when no DB is configured.
[ApiController]
[Route("api/admin")]
[EnableRateLimiting("write")]
public sealed class AdminController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase
{
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
    private IdentityApiClient? Identity => services.GetService(typeof(IdentityApiClient)) as IdentityApiClient;

    private IActionResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    public sealed record SetRole(string Role);

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        if (RequireAdmin() is { } no) return no;
        var identity = Identity; if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var rows = users.Select(u => new { u.DiscordId, u.Username, u.Role, u.LastLoginAt });
        return Ok(rows);
    }

    // Last 60 one-minute buckets (total requests + 429s per minute), in-process ring.
    [HttpGet("metrics")]
    [EnableRateLimiting("read")]
    public IActionResult Metrics()
    {
        if (RequireAdmin() is { } no) return no;
        var metrics = services.GetService(typeof(EggIncognito.Services.Metrics.ApiMetrics))
            as EggIncognito.Services.Metrics.ApiMetrics;
        if (metrics is null) return Ok(Array.Empty<object>());
        var pts = metrics.Snapshot().Select(p => new { minute = p.Minute, total = p.Total, limited = p.Limited });
        return Ok(pts);
    }

    // Empty list when capture is not wired (e.g. a hosted instance with capture off).
    [HttpGet("sessions")]
    [EnableRateLimiting("read")]
    public IActionResult Sessions()
    {
        if (RequireAdmin() is { } no) return no;
        var mgr = services.GetService(typeof(EggIncognito.Capture.CaptureSessionManager))
            as EggIncognito.Capture.CaptureSessionManager;
        if (mgr is null) return Ok(Array.Empty<object>());
        var rows = mgr.All().Select(x =>
        {
            var s = x.Session.Hub.StatsSnapshot();
            return new
            {
                key = x.Key,
                running = s.Running,
                port = s.Port,
                flows = s.CapturedAuxbrain,
                connections = s.ActiveConnections,
                devices = s.DeviceCount,
                decryptOk = s.DecryptOk,
                decryptErr = s.DecryptErrors,
            };
        }).ToList();
        return Ok(rows);
    }

    // The local key stops but is not removed: it is the shared anonymous session.
    [HttpDelete("sessions/{key}")]
    public async Task<IActionResult> KillSession(string key)
    {
        if (RequireAdmin() is { } no) return no;
        var mgr = services.GetService(typeof(EggIncognito.Capture.CaptureSessionManager))
            as EggIncognito.Capture.CaptureSessionManager;
        if (mgr is null) return StatusCode(503, new { error = "capture not configured" });
        var session = mgr.Get(key);
        if (session is null) return NotFound(new { error = "session not found" });
        await session.StopAsync();
        if (key != EggIncognito.Capture.CaptureSessionManager.LocalKey) mgr.Remove(key);
        return Ok(new { killed = key });
    }

    [HttpPost("users/{discordId}/role")]
    public async Task<IActionResult> SetUserRole(string discordId, [FromBody] SetRole body)
    {
        if (RequireAdmin() is { } no) return no;
        var role = (body.Role ?? "").Trim().ToLowerInvariant();
        if (role is not ("viewer" or "contributor" or "admin"))
            return BadRequest(new { error = $"unknown role '{body.Role}'" });
        if (discordId == currentUser.DiscordId && role != "admin")
            return BadRequest(new { error = "cannot remove your own admin role" });

        var identity = Identity; if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var user = users.FirstOrDefault(u => u.DiscordId == discordId);
        if (user is null) return NotFound(new { error = "user not found" });
        await identity.SetRoleAsync(user.UserId, role, HttpContext.RequestAborted);
        return Ok(new { discordId, role });
    }

    // One-shot: backfills UserId on capture_proxy_addrs/capture_user_cas rows minted before those
    // tables were keyed by user id. Run before the RepointCaptureTablesToUserId migration.
    [HttpPost("backfill-capture-user-ids")]
    public async Task<IActionResult> BackfillCaptureUserIds(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity; if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var updated = await CaptureUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

    // One-shot: backfills owner_user_id/author_user_id on docs/doc_images/env_designs/
    // env_design_versions/stored_endpoints/stored_routes/feed_subscriptions rows still holding a
    // Discord ID string. Run before the RetypeOwnerAuthorUserIdColumns migration.
    [HttpPost("backfill-owner-author-user-ids")]
    public async Task<IActionResult> BackfillOwnerAuthorUserIds(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity; if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var updated = await OwnerAuthorUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
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

    // Tag definitions are catalog-level data, admin-only; contributors only assign existing tags.
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

    // Deleting a tag also clears its subject_tags join rows: no hard FK, so it's done in app.
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
