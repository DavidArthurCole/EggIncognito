using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

// Read + write API for documentation and tags attached to API "subjects", a proto message type or an
// endpoint path. Mirrors StoredEndpointController's posture: reads are public and degrade to empty
// when no DB is configured; writes require contributor+ and 503 when no DB. The role gate runs before
// the DB resolve so a viewer 403s regardless of DB state. Creating tag definitions is an admin op in
// AdminController; here a contributor only assigns existing tags to subjects.
[ApiController]
[Route("api/docs")]
[EnableRateLimiting("write")]
public sealed class DocsController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase
{
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IActionResult? RequireContributor() =>
        currentUser.IsAtLeast(UserRole.Contributor)
            ? null
            : StatusCode(403, new { error = "contributor role required to edit documentation" });

    // Valid subject kinds. Widened for the docs hub: the registry also exposes route, config, control.
    private static bool ValidKind(string kind) =>
        kind is "message" or "endpoint" or "route" or "config" or "control";

    // Mark a read response browser-cacheable briefly so the SPA's per-load fetches don't re-query the
    // DB each navigation. Short TTL keeps edits surfacing quickly; private = per-browser, never shared.
    private void CacheFor(int seconds) =>
        Response.Headers.CacheControl = $"private, max-age={seconds}";

    public sealed record UpsertDoc(string SubjectKind, string SubjectKey, string BodyMd);
    public sealed record SetSubjectTags(string SubjectKind, string SubjectKey, long[] TagIds);

    // GET /api/docs/doc/{kind}/{**key} - the doc for a subject, or { bodyMd: null } when none. key is a
    // catch-all because endpoint paths contain slashes.
    [HttpGet("doc/{kind}/{**key}")]
    public async Task<IActionResult> GetDoc(string kind, string key)
    {
        if (!ValidKind(kind)) return BadRequest(new { error = "invalid subject kind" });
        var db = Db;
        if (db is null) return Ok(new { bodyMd = (string?)null });
        var doc = await db.Docs.AsNoTracking()
            .FirstOrDefaultAsync(d => d.SubjectKind == kind && d.SubjectKey == key);
        return Ok(doc is null
            ? new { bodyMd = (string?)null }
            : new { bodyMd = doc.BodyMd, updatedAt = (object)doc.UpdatedAt, owner = (object?)doc.OwnerUserId });
    }

    // POST /api/docs/doc - upsert a subject's doc (contributor+). An empty/whitespace body deletes it.
    [HttpPost("doc")]
    public async Task<IActionResult> UpsertDocAsync([FromBody] UpsertDoc body)
    {
        if (RequireContributor() is { } no) return no;
        if (!ValidKind(body.SubjectKind)) return BadRequest(new { error = "invalid subject kind" });
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });

        var existing = await db.Docs
            .FirstOrDefaultAsync(d => d.SubjectKind == body.SubjectKind && d.SubjectKey == body.SubjectKey);
        var empty = string.IsNullOrWhiteSpace(body.BodyMd);

        if (existing is null)
        {
            if (empty) return Ok(new { saved = false }); // nothing to store
            db.Docs.Add(new Doc
            {
                SubjectKind = body.SubjectKind, SubjectKey = body.SubjectKey,
                BodyMd = body.BodyMd, OwnerUserId = currentUser.DiscordId,
            });
        }
        else if (empty)
        {
            db.Docs.Remove(existing); // clearing the body removes the doc
        }
        else
        {
            existing.BodyMd = body.BodyMd;
            existing.UpdatedAt = System.DateTimeOffset.UtcNow; // default only applies on insert
        }
        await db.SaveChangesAsync();
        return Ok(new { saved = !empty });
    }

    // GET /api/docs/tags - the tag catalog (public; [] when no DB).
    [HttpGet("tags")]
    public async Task<IActionResult> GetTags()
    {
        var db = Db;
        if (db is null) return Ok(System.Array.Empty<object>());
        var rows = await db.Tags.AsNoTracking().OrderBy(t => t.Label)
            .Select(t => new { t.Id, t.Slug, t.Label, t.Color }).ToListAsync();
        CacheFor(30);
        return Ok(rows);
    }

    // GET /api/docs/subject-tags/{kind}/{**key} - the tag objects applied to one subject.
    [HttpGet("subject-tags/{kind}/{**key}")]
    public async Task<IActionResult> GetSubjectTags(string kind, string key)
    {
        if (!ValidKind(kind)) return BadRequest(new { error = "invalid subject kind" });
        var db = Db;
        if (db is null) return Ok(System.Array.Empty<object>());
        var rows = await (
            from st in db.SubjectTags.AsNoTracking()
            where st.SubjectKind == kind && st.SubjectKey == key
            join t in db.Tags.AsNoTracking() on st.TagId equals t.Id
            orderby t.Label
            select new { t.Id, t.Slug, t.Label, t.Color }).ToListAsync();
        CacheFor(30);
        return Ok(rows);
    }

    // POST /api/docs/subject-tags - replace the full tag set for a subject (contributor+).
    [HttpPost("subject-tags")]
    public async Task<IActionResult> SetSubjectTagsAsync([FromBody] SetSubjectTags body)
    {
        if (RequireContributor() is { } no) return no;
        if (!ValidKind(body.SubjectKind)) return BadRequest(new { error = "invalid subject kind" });
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });

        var wanted = (body.TagIds ?? []).Distinct().ToHashSet();
        // Drop ids that don't reference a real tag, so a bad client can't create dangling joins.
        if (wanted.Count > 0)
        {
            var real = await db.Tags.Where(t => wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            wanted = real.ToHashSet();
        }

        var current = await db.SubjectTags
            .Where(s => s.SubjectKind == body.SubjectKind && s.SubjectKey == body.SubjectKey)
            .ToListAsync();
        var currentIds = current.Select(s => s.TagId).ToHashSet();

        db.SubjectTags.RemoveRange(current.Where(s => !wanted.Contains(s.TagId)));
        foreach (var id in wanted.Where(id => !currentIds.Contains(id)))
            db.SubjectTags.Add(new SubjectTag { SubjectKind = body.SubjectKind, SubjectKey = body.SubjectKey, TagId = id });

        await db.SaveChangesAsync();
        return Ok(new { tagIds = wanted });
    }

    // GET /api/docs/tags-map - every subject's tags in one batch, so the SPA can render chips across a
    // whole list without N round-trips. Shape: { "message:Contract": [tag...], ... }.
    [HttpGet("tags-map")]
    public async Task<IActionResult> GetTagsMap()
    {
        var db = Db;
        if (db is null) return Ok(new Dictionary<string, object>());
        var rows = await (
            from st in db.SubjectTags.AsNoTracking()
            join t in db.Tags.AsNoTracking() on st.TagId equals t.Id
            select new { st.SubjectKind, st.SubjectKey, t.Id, t.Slug, t.Label, t.Color }).ToListAsync();

        var map = rows
            .GroupBy(r => $"{r.SubjectKind}:{r.SubjectKey}")
            .ToDictionary(
                g => g.Key,
                g => (object)g.OrderBy(r => r.Label)
                    .Select(r => new { r.Id, r.Slug, r.Label, r.Color }).ToList());
        CacheFor(30);
        return Ok(map);
    }

    // Images: uploaded inline-doc images live in Postgres bytea so they work in the read-only Hosted
    // deploy with no filesystem writes. Upload is contributor+; serving is public. Markdown references
    // them by /api/docs/image/{id}, which safeUrl() in md.js allows as a relative URL.

    private const int MaxImageBytes = 4 * 1024 * 1024; // 4 MB cap
    // Raster only. SVG is deliberately excluded: an SVG opened directly can execute script, a
    // stored-XSS vector not worth it for doc images.
    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/png", "image/jpeg", "image/gif", "image/webp" };

    // POST /api/docs/image - multipart upload of one image file. Returns { url, id }.
    [HttpPost("image")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)] // body cap a touch above the byte cap, for multipart overhead
    public async Task<IActionResult> UploadImageAsync(IFormFile? file)
    {
        if (RequireContributor() is { } no) return no;
        if (file is null || file.Length == 0) return BadRequest(new { error = "no file" });
        if (file.Length > MaxImageBytes) return BadRequest(new { error = $"image exceeds {MaxImageBytes / (1024 * 1024)} MB" });
        var ct = file.ContentType ?? "";
        if (!AllowedImageTypes.Contains(ct)) return BadRequest(new { error = $"unsupported content type '{ct}'" });

        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var bytes = ms.ToArray();

        var img = new DocImage
        {
            ContentType = ct, Bytes = bytes, ByteSize = bytes.Length,
            OwnerUserId = currentUser.DiscordId,
        };
        db.DocImages.Add(img);
        await db.SaveChangesAsync();
        return Ok(new { id = img.Id, url = $"/api/docs/image/{img.Id}" });
    }

    // GET /api/docs/image/{id} - serve the stored bytes (public). 404 when missing or no DB.
    [HttpGet("image/{id:long}")]
    public async Task<IActionResult> GetImage(long id)
    {
        var db = Db;
        if (db is null) return NotFound();
        var img = await db.DocImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (img is null) return NotFound();
        // nosniff so the browser honors the declared raster type; long immutable cache since bytes
        // never change for an id.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(img.Bytes, img.ContentType);
    }

    // GET /api/docs/has - which subjects HAVE a doc, so the SPA can mark them in the list without
    // fetching each body. Shape: { "message:Contract": true, ... }.
    [HttpGet("has")]
    public async Task<IActionResult> GetHasDocs()
    {
        var db = Db;
        if (db is null) return Ok(new Dictionary<string, bool>());
        var keys = await db.Docs.AsNoTracking()
            .Select(d => new { d.SubjectKind, d.SubjectKey }).ToListAsync();
        var map = keys.ToDictionary(k => $"{k.SubjectKind}:{k.SubjectKey}", _ => true);
        CacheFor(30);
        return Ok(map);
    }
}
