using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/v1/data")]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class DataApiController(DataCatalog catalog, ICurrentUser currentUser) : ControllerBase {
    [HttpGet]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Index(CancellationToken ct) {
        var listed = catalog.Sources.Where(s => s.Listed).ToList();
        var items = new List<object>(listed.Count);
        foreach (var s in listed) {
            items.Add(new {
                s.Id,
                s.Group,
                url = catalog.UrlFor(s),
                displayName = s.DisplayName,
                description = s.Description,
                provenance = s.Provenance.ToString(),
                access = s.Access.ToString(),
                feed = s.Feed,
                extends = s.Extends,
                acceptsName = s.AcceptsName,
                bytes = await SizeOf(s, ct),
                refresh = new { s.Refresh.Egress, deviceTrigger = s.Refresh.Device is not null }
            });
        }

        return Ok(new { count = listed.Count, sources = items });
    }

    private async Task<long?> SizeOf(DataSource s, CancellationToken ct) {
        if (s.AcceptsName) return null;
        try {
            var payload = await s.Produce(new DataProduceContext(HttpContext, null), ct);
            return payload?.Bytes.LongLength;
        } catch {
            return null;
        }
    }

    [HttpGet("{group}/{id}")]
    [EnableRateLimiting("data")]
    public async Task<IActionResult> Get(string group, string id, [FromQuery] string? name, CancellationToken ct) {
        var src = catalog.ById(group, id);
        if (src is null) return NotFound(new { error = "unknown data source", group, id });
        return src.Extends is not null
            ? NotFound(new { error = "this is an extension dataset", url = catalog.UrlFor(src) })
            : await Serve(src, name, ct);
    }

    [HttpGet("{group}/{parent}/{sub}")]
    [EnableRateLimiting("data")]
    public async Task<IActionResult> GetExtension(string group, string parent, string sub, [FromQuery] string? name,
        CancellationToken ct) {
        var src = catalog.ByChild(group, parent, sub);
        return src is null
            ? NotFound(new { error = "unknown extension dataset", group, parent, sub })
            : await Serve(src, name, ct);
    }

    private async Task<IActionResult> Serve(DataSource src, string? name, CancellationToken ct) {
        if (src.Access == DataAccess.Authenticated && !currentUser.IsAuthenticated) {
            return StatusCode(401,
                new { error = "authentication required", hint = "mint an API key at /api/v1/keys or log in" });
        }

        if (src.AcceptsName && string.IsNullOrEmpty(name))
            return BadRequest(new { error = "this source requires a name query parameter" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, name), ct);
        if (payload is null) return NotFound(new { error = "data not available", id = src.Id });

        var maxAge = src.Provenance == DataProvenance.Asset ? TimeSpan.FromDays(30) : TimeSpan.FromSeconds(30);
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = maxAge };
        Response.Headers["X-Data-Source"] = src.Id;
        return File(payload.Bytes, payload.ContentType);
    }
}
