using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Services;
using EggIncognito.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SyncKit.Identity.Client;

namespace EggIncognito.Controllers;

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

   
   
    [HttpGet("sessions")]
    [EnableRateLimiting("read")]
    public IActionResult Sessions()
    {
        if (RequireAdmin() is { } no) return no;
        var rows = new List<object>();

        if (services.GetService(typeof(EggIncognito.Capture.CaptureSessionManager))
            is EggIncognito.Capture.CaptureSessionManager mgr)
        {
            rows.AddRange(mgr.All().Select(x =>
            {
                var s = x.Session.Hub.StatsSnapshot();
                return (object)new
                {
                    key = x.Key,
                    kind = x.Key == EggIncognito.Capture.CaptureSessionManager.LocalKey ? "local" : "user",
                    killable = true,
                    running = s.Running,
                    port = s.Port,
                    flows = s.CapturedAuxbrain,
                    connections = s.ActiveConnections,
                    devices = s.DeviceCount,
                    decryptOk = s.DecryptOk,
                    decryptErr = s.DecryptErrors,
                };
            }));
        }

        if (services.GetService(typeof(EggIncognito.Services.Devices.DeviceCaptureManager))
                is EggIncognito.Services.Devices.DeviceCaptureManager dcm
            && services.GetService(typeof(EggIncognito.Services.Devices.DeviceConfig))
                is EggIncognito.Services.Devices.DeviceConfig devCfg)
        {
            rows.AddRange(devCfg.Devices.Select(d =>
            {
                var diag = dcm.DiagFor(d.Id);
                var port = dcm.PortFor(d.Id);
                return (object)new
                {
                    key = $"device:{d.Id}",
                    kind = "device",
                    killable = false,
                    running = port != 0,
                    port,
                    flows = diag.Flows,
                    connections = diag.ClientConnects,
                    devices = 1,
                    decryptOk = diag.RinfoHarvests,
                    decryptErr = diag.LastDecryptError is null ? 0 : 1,
                };
            }));
        }

        return Ok(rows);
    }

    [HttpGet("data-status")]
    [EnableRateLimiting("read")]
    public IActionResult DataStatus()
    {
        if (RequireAdmin() is { } no) return no;

        var gameData = new List<object>();
        if (services.GetService(typeof(IGameDataProvider)) is IGameDataProvider provider)
        {
            foreach (var f in provider.Families)
                gameData.Add(new
                {
                    key = f.Key,
                    count = f.Effects.Count,
                    provenance = (f as EmbeddedEffectFamily)?.Status ?? "",
                });
            var col = provider.Colleggtibles;
            gameData.Add(new
            {
                key = "colleggtibles",
                count = col.Eggs.Count,
                provenance = string.IsNullOrEmpty(col.BinaryVersion) ? col.Status : col.BinaryVersion,
            });
        }

        var platforms = Array.Empty<object>();
        var configEnabled = false;
        if (services.GetService(typeof(GameConfigStore)) is GameConfigStore store)
        {
            configEnabled = store.Enabled;
            platforms = store.List()
                .Select(c => (object)new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes })
                .ToArray();
        }

        var fixtures = new List<object>();
        if (services.GetService(typeof(IConfiguration)) is IConfiguration cfg)
        {
            var eiDir = Path.Combine(ContentRoot.Resolve(cfg["ContentRoot"]), "Endpoints", "default", "ei");
            if (Directory.Exists(eiDir))
            {
                foreach (var path in Directory.EnumerateFiles(eiDir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                {
                    var info = new FileInfo(path);
                    string status;
                    try
                    {
                        var trimmed = System.IO.File.ReadAllText(path).Trim();
                        status = trimmed.Length == 0 || trimmed == "{}" ? "empty" : "ok";
                    }
                    catch { status = "unreadable"; }
                    fixtures.Add(new
                    {
                        name = Path.GetFileNameWithoutExtension(info.Name),
                        bytes = info.Length,
                        updatedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        status,
                    });
                }
            }
        }

        return Ok(new { gameData, config = new { enabled = configEnabled, platforms }, fixtures });
    }


    private EggIncognito.Services.Metrics.ApiAuditLog? Audit =>
        services.GetService(typeof(EggIncognito.Services.Metrics.ApiAuditLog))
            as EggIncognito.Services.Metrics.ApiAuditLog;

    [HttpGet("audit/recent")]
    [EnableRateLimiting("read")]
    public IActionResult AuditRecent([FromQuery] int take = 200)
    {
        if (RequireAdmin() is { } no) return no;
        var a = Audit; if (a is null) return Ok(Array.Empty<object>());
        var rows = a.Recent(take).Select(e => new
        {
            ts = e.Ts,
            method = e.Method,
            path = e.Path,
            status = e.Status,
            bucket = e.Bucket.ToString(),
            ip = e.Ip,
            user = e.User,
        });
        return Ok(rows);
    }

    [HttpGet("audit/paths")]
    [EnableRateLimiting("read")]
    public IActionResult AuditPaths()
    {
        if (RequireAdmin() is { } no) return no;
        var a = Audit; if (a is null) return Ok(Array.Empty<object>());
        var rows = a.Paths().Select(p => new
        {
            path = p.Path,
            total = p.Roll.Total,
            @internal = p.Roll.Internal,
            cross = p.Roll.Cross,
            external = p.Roll.External,
            lastSeen = new DateTimeOffset(System.Threading.Volatile.Read(ref p.Roll.LastSeenTicks), TimeSpan.Zero),
        });
        return Ok(rows);
    }

    [HttpGet("audit/ips")]
    [EnableRateLimiting("read")]
    public IActionResult AuditIps()
    {
        if (RequireAdmin() is { } no) return no;
        var a = Audit; if (a is null) return Ok(Array.Empty<object>());
        var rows = a.Ips().Select(x => new { ip = x.Ip, total = x.Total, distinctPaths = x.DistinctPaths, lastSeen = x.LastSeen });
        return Ok(rows);
    }

    [HttpGet("audit/buckets")]
    [EnableRateLimiting("read")]
    public IActionResult AuditBuckets()
    {
        if (RequireAdmin() is { } no) return no;
        var a = Audit; if (a is null) return Ok(new { @internal = 0, cross = 0, external = 0, keysCapped = 0 });
        var (i, c, e) = a.Buckets();
        return Ok(new { @internal = i, cross = c, external = e, keysCapped = a.KeysCapped });
    }

   
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

   
   
    [HttpPost("backfill-capture-user-ids")]
    public async Task<IActionResult> BackfillCaptureUserIds(CancellationToken ct)
    {
        if (RequireAdmin() is { } no) return no;
        var db = Db; if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity; if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var updated = await CaptureUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

   
   
   
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
