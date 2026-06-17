using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Catch-all for POST paths that no generated controller claimed. Kept as the single
// [HttpPost("/{**slug}")] (a second POST catch-all on the same template would be an ambiguous
// match), resolving in order:
//   1. DB-only routes added at runtime (source = "db" in stored_routes).
//   2. Aliases of mocked routes (routes.yaml `aliases:`), served exactly as the canonical path.
//   3. Known-namespace fallback: a real-API path from auxbrain-paths.json with a known response
//      type gets an empty proto encoded like a normal mock response; any other POST under a known
//      auxbrain namespace gets a deterministic 200 not-mocked marker, never a hard 404.
// Paths outside the known namespaces keep the old behavior (404). Attribute routing ranks the
// generated controllers' concrete templates above this {**slug} catch-all, so yaml routes never
// reach here. POST-only, so it never shadows SimulationController's OPTIONS catch-all.
[ApiController]
public sealed class DynamicMockController(
    IRouteCatalog routes,
    IEndpointStore endpoints,
    AuxbrainSurface surface) : ControllerBase
{
    [HttpPost("/{**slug}")]
    public IActionResult Handle(string slug, [FromForm] string? data)
    {
        var route = routes.Get(slug);
        if (route is not null && Serve(route, data) is { } stored) return stored;

        // routes.yaml aliases: old request paths kept after a rename to the canonical auxbrain
        // path. The generator emits no controller for an alias, so they all land here and serve
        // the canonical route's response (same EndpointStore lookup + encode semantics).
        if (surface.ResolveAlias(slug) is { } canonical && Serve(canonical, data) is { } aliased)
            return aliased;

        if (!surface.IsKnownNamespace(slug)) return NotFound();

        // Real-API path with a known response type but no mock route yet: empty message,
        // normal mock framing. The store lookup keeps it upgradeable by dropping a JSON file.
        if (surface.Canonical.TryGetValue(slug, out var c) && c.ResponseType is not null
            && ProtoTypeResolver.Resolve(c.ResponseType) is { } type)
            return Encode(endpoints.Get(type, slug, EidExtractor.FromData(data)));

        Response.Headers["x-eggincognito"] = "not-mocked";
        return Ok(new { notMocked = true, path = slug });
    }

    // Serve route response; null when type cannot be resolved (caller falls through).
    private IActionResult? Serve(RouteInfo route, string? data)
    {
        if (route.RawResponse is not null) return Content(route.RawResponse, "text/plain");
        var type = ProtoTypeResolver.Resolve(route.Response ?? "AuthenticatedMessage");
        if (type is null) return null;
        return Encode(endpoints.Get(type, route.Path, EidExtractor.FromData(data)));
    }

    private ContentResult Encode(IMessage message) =>
        Content(Convert.ToBase64String(message.ToByteArray()), "text/html");
}
