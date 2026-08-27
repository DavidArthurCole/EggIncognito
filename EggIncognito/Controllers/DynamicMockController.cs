using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;

namespace EggIncognito.Controllers;

[ApiController]
[ApiAccess(ApiAccessLevel.Public)]
public sealed class DynamicMockController(
    IRouteCatalog routes,
    IEndpointStore endpoints,
    AuxbrainSurface surface) : ControllerBase {
    [HttpPost("/{**slug}")]
    public IActionResult Handle(string slug, [FromForm] string? data) {
        var route = routes.Resolve(slug);
        if (route is not null && Serve(route, data) is { } stored) return stored;

        if (surface.ResolveAlias(slug) is { } canonical && Serve(canonical, data) is { } aliased)
            return aliased;

        if (!surface.IsKnownNamespace(slug)) return NotFound();

        Response.Headers["x-eggincognito"] = "not-mocked";
        return Ok(new { notMocked = true, path = slug });
    }

    private ContentResult? Serve(RouteInfo route, string? data) {
        if (route.RawResponse is not null) return Content(route.RawResponse, "text/plain");
        var type = ProtoTypeResolver.Resolve(route.Response ?? "AuthenticatedMessage");
        return type is null ? null : Encode(endpoints.Fetch(type, route.Path, EidExtractor.FromData(data)));
    }

    private ContentResult Encode(IMessage message) =>
        Content(Convert.ToBase64String(message.ToByteArray()), "text/html");
}
