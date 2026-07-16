using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;

namespace EggIncognito.Controllers;


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

       
       
        if (surface.ResolveAlias(slug) is { } canonical && Serve(canonical, data) is { } aliased)
            return aliased;

        if (!surface.IsKnownNamespace(slug)) return NotFound();

       
       
        if (surface.Canonical.TryGetValue(slug, out var c) && c.ResponseType is not null
            && ProtoTypeResolver.Resolve(c.ResponseType) is { } type)
            return Encode(endpoints.Get(type, slug, EidExtractor.FromData(data)));

        Response.Headers["x-eggincognito"] = "not-mocked";
        return Ok(new { notMocked = true, path = slug });
    }

   
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
