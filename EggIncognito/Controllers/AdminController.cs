using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.Capture;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Devices;
using EggIncognito.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using EggIdentity.Contract;
using EggIdentity.Client;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class AdminController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase {
    private static readonly JsonSerializerOptions ProvenanceJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
    private IdentityApiClient? Identity => services.GetService(typeof(IdentityApiClient)) as IdentityApiClient;

    private ApiKeyStore? Keys => services.GetService(typeof(ApiKeyStore)) as ApiKeyStore;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("users")]
    public async Task<IActionResult> Users() {
        if (RequireAdmin() is { } no) return no;
        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var rows = users.Select(u => new { u.DiscordId, u.Username, u.Role, u.LastLoginAt });
        return Ok(rows);
    }

    [HttpGet("api-keys")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ApiKeys(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Keys;
        if (store is null) return Ok(Array.Empty<object>());
        var rows = await store.AllAsync(ct);
        return Ok(rows.Select(k => new {
            k.Id,
            k.Name,
            k.Prefix,
            k.OwnerUserId,
            k.CreatedAt,
            k.LastUsedAt,
            k.RequestCount,
            k.Revoked,
            k.RevokedAt
        }));
    }

    [HttpDelete("api-keys/{id:int}")]
    public async Task<IActionResult> RevokeApiKey(int id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Keys;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        bool ok = await store.AdminRevokeAsync(id, ct);
        if (!ok) return NotFound(new { error = "key not found" });
        return Ok(new { revoked = true });
    }

    [HttpDelete("api-keys/{id:int}/purge")]
    public async Task<IActionResult> DeleteApiKey(int id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Keys;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        bool ok = await store.AdminDeleteAsync(id, ct);
        if (!ok) return NotFound(new { error = "key not found" });
        return Ok(new { deleted = true });
    }


    [HttpGet("sessions")]
    [EnableRateLimiting("read")]
    public IActionResult Sessions() {
        if (RequireAdmin() is { } no) return no;
        var rows = new List<object>();

        if (services.GetService(typeof(CaptureSessionManager))
            is CaptureSessionManager mgr) {
            rows.AddRange(mgr.All().Select(x => {
                var s = x.Session.Hub.StatsSnapshot();
                return (object)new {
                    key = x.Key,
                    kind = x.Key == CaptureSessionManager.LocalKey ? "local" : "user",
                    killable = true,
                    running = s.Running,
                    port = s.Port,
                    flows = s.CapturedAuxbrain,
                    connections = s.ActiveConnections,
                    devices = s.DeviceCount,
                    decryptOk = s.DecryptOk,
                    decryptErr = s.DecryptErrors
                };
            }));
        }

        if (services.GetService(typeof(DeviceCaptureManager))
                is DeviceCaptureManager dcm
            && services.GetService(typeof(DeviceConfig))
                is DeviceConfig devCfg) {
            rows.AddRange(devCfg.Devices.Select(d => {
                var diag = dcm.DiagFor(d.Id);
                int port = dcm.PortFor(d.Id);
                return (object)new {
                    key = $"device:{d.Id}",
                    kind = "device",
                    killable = false,
                    running = port != 0,
                    port,
                    flows = diag.Flows,
                    connections = diag.ClientConnects,
                    devices = 1,
                    decryptOk = diag.RinfoHarvests,
                    decryptErr = diag.LastDecryptError is null ? 0 : 1
                };
            }));
        }

        return Ok(rows);
    }

    [HttpGet("data-status")]
    [EnableRateLimiting("read")]
    public IActionResult DataStatus() {
        if (RequireAdmin() is { } no) return no;

        var gameData = new List<object>();
        if (services.GetService(typeof(IGameDataProvider)) is IGameDataProvider provider) {
            foreach (var f in provider.Families) {
                gameData.Add(new {
                    key = f.Key,
                    count = f.Effects.Count,
                    provenance = JsonSerializer.Serialize(f.Provenance, ProvenanceJson)
                });
            }

            var col = provider.Colleggtibles;
            gameData.Add(new {
                key = "colleggtibles",
                count = col.Eggs.Count,
                gameVersion = col.GameVersion,
                provenance = JsonSerializer.Serialize(col.Provenance, ProvenanceJson)
            });
        }

        object[] platforms = [];
        bool configEnabled = false;
        if (services.GetService(typeof(GameConfigStore)) is GameConfigStore store) {
            configEnabled = store.Enabled;
            platforms = [
                .. store.List().Select(c => (object)new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes })
            ];
        }

        var fixtures = new List<object>();
        if (services.GetService(typeof(IConfiguration)) is IConfiguration cfg) {
            string eiDir = Path.Combine(ContentRoot.Resolve(cfg["ContentRoot"]), "Endpoints", "default", "ei");
            if (Directory.Exists(eiDir)) {
                foreach (string path in Directory.EnumerateFiles(eiDir, "*.json")
                             .OrderBy(p => p, StringComparer.Ordinal)) {
                    var info = new FileInfo(path);
                    string status;
                    try {
                        string trimmed = System.IO.File.ReadAllText(path).Trim();
                        status = trimmed.Length == 0 || trimmed == "{}" ? "empty" : "ok";
                    } catch {
                        status = "unreadable";
                    }

                    fixtures.Add(new {
                        name = Path.GetFileNameWithoutExtension(info.Name),
                        bytes = info.Length,
                        updatedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        status
                    });
                }
            }
        }

        return Ok(new { gameData, config = new { enabled = configEnabled, platforms }, fixtures });
    }


    [HttpDelete("sessions/{key}")]
    public async Task<IActionResult> KillSession(string key) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(CaptureSessionManager)) is not CaptureSessionManager mgr)
            return StatusCode(503, new { error = "capture not configured" });
        var session = mgr.Get(key);
        if (session is null) return NotFound(new { error = "session not found" });
        await session.StopAsync();
        if (key != CaptureSessionManager.LocalKey) mgr.Remove(key);
        return Ok(new { killed = key });
    }

    [HttpPost("users/{discordId}/role")]
    public async Task<IActionResult> SetUserRole(string discordId, [FromBody] SetRole body) {
        if (RequireAdmin() is { } no) return no;
        string role = (body.Role ?? "").Trim().ToLowerInvariant();
        if (role is not ("viewer" or "contributor" or "admin"))
            return BadRequest(new { error = $"unknown role '{body.Role}'" });
        if (discordId == currentUser.DiscordId && role != "admin")
            return BadRequest(new { error = "cannot remove your own admin role" });

        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var user = users.FirstOrDefault(u => u.DiscordId == discordId);
        if (user is null) return NotFound(new { error = "user not found" });
        await identity.SetRoleAsync(user.UserId, role, HttpContext.RequestAborted);
        return Ok(new { discordId, role });
    }


    [HttpPost("backfill-capture-user-ids")]
    public async Task<IActionResult> BackfillCaptureUserIds(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        int updated = await CaptureUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }


    [HttpPost("backfill-owner-author-user-ids")]
    public async Task<IActionResult> BackfillOwnerAuthorUserIds(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        int updated = await OwnerAuthorUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

    [HttpDelete("endpoint/{id:long}")]
    public async Task<IActionResult> DeleteEndpoint(long id) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var row = await db.StoredEndpoints.FindAsync(id);
        if (row is null) return NotFound();
        db.StoredEndpoints.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    [HttpDelete("route/{id:long}")]
    public async Task<IActionResult> DeleteRoute(long id) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var row = await db.StoredRoutes.FindAsync(id);
        if (row is null) return NotFound();
        if (row.Source != "db") return BadRequest(new { error = "cannot delete a yaml-sourced route" });
        db.StoredRoutes.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }


    [HttpPost("tag")]
    public async Task<IActionResult> AddTagAsync([FromBody] AddTag body) {
        if (RequireAdmin() is { } no) return no;
        string slug = (body.Slug ?? "").Trim().ToLowerInvariant();
        string label = (body.Label ?? "").Trim();
        if (slug.Length == 0 || label.Length == 0) return BadRequest(new { error = "slug and label are required" });

        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (await db.Tags.AnyAsync(t => t.Slug == slug)) return Conflict(new { error = $"tag {slug} already exists" });

        var tag = new Tag { Slug = slug, Label = label, Color = string.IsNullOrWhiteSpace(body.Color) ? null : body.Color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return Ok(new { tag.Id, tag.Slug, tag.Label, tag.Color });
    }


    [HttpDelete("tag/{id:long}")]
    public async Task<IActionResult> DeleteTag(long id) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var tag = await db.Tags.FindAsync(id);
        if (tag is null) return NotFound();
        var joins = await db.SubjectTags.Where(s => s.TagId == id).ToListAsync();
        db.SubjectTags.RemoveRange(joins);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    public sealed record SetRole(string Role);

    public sealed record AddTag(string Slug, string Label, string? Color);
}
