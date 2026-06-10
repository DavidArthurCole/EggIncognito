using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

// Catch-all for POST paths that no generated controller claimed - i.e. DB-only routes added at
// runtime (source = "db" in stored_routes). Attribute routing ranks the generated controllers'
// concrete templates above this {**slug} catch-all, so yaml routes never reach here. POST-only, so
// it never shadows SimulationController's OPTIONS catch-all. Resolves the response proto type from
// the merged route catalog, materializes it from the endpoint store (DB overlay), and returns the
// same base64 text framing the generated controllers use.
[ApiController]
public sealed class DynamicMockController(IRouteCatalog routes, IEndpointStore endpoints) : ControllerBase
{
    [HttpPost("/{**slug}")]
    public IActionResult Handle(string slug, [FromForm] string? data)
    {
        var route = routes.Get(slug);
        if (route is null) return NotFound();

        if (route.RawResponse is not null)
            return Content(route.RawResponse, "text/plain");

        if (route.Response is null) return NotFound(); // unknown inner type -> cannot encode
        var type = ProtoTypeResolver.Resolve(route.Response);
        if (type is null) return NotFound();

        var eid = EidExtractor.FromData(data);
        var message = endpoints.Get(type, slug, eid);
        var encoded = Convert.ToBase64String(message.ToByteArray());
        return Content(encoded, "text/html");
    }
}
