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
public sealed class DataApiController(DataCatalog catalog, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [EnableRateLimiting("read")]
    public IActionResult Index()
    {
        var items = catalog.Sources.Select(s => new
        {
            s.Id,
            s.Group,
            url = catalog.UrlFor(s),
            displayName = s.DisplayName,
            description = s.Description,
            provenance = s.Provenance.ToString(),
            access = s.Access.ToString(),
            feed = s.Feed,
            acceptsName = s.AcceptsName,
            refresh = new { s.Refresh.Egress, deviceTrigger = s.Refresh.Device is not null },
        });
        return Ok(new { count = catalog.Sources.Count, sources = items });
    }

    [HttpGet("{group}/{id}")]
    [EnableRateLimiting("data")]
    public async Task<IActionResult> Get(string group, string id, [FromQuery] string? name, CancellationToken ct)
    {
        var src = catalog.ById(group, id);
        if (src is null) return NotFound(new { error = "unknown data source", group, id });

        if (src.Access == DataAccess.Authenticated && !currentUser.IsAuthenticated)
            return StatusCode(401, new { error = "authentication required", hint = "mint an API key at /api/v1/keys or log in" });

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
