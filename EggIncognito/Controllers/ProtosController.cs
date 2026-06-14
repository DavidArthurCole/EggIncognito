using EggIncognito.Data.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

// Read-only public registry surface over proto_versions + proto_protos. Proto definitions are not
// secret, so reads are public under the shared read limiter. Writes happen only via the authed farm
// event. No DB configured => empty list / 404, mirroring ToolsController's DB-free tolerance.
[ApiController]
[Route("api/protos")]
[EnableRateLimiting("read")]
public sealed class ProtosController(IServiceProvider services) : ControllerBase
{
    private ProtoRegistryStore? Store =>
        services.GetService(typeof(ProtoRegistryStore)) as ProtoRegistryStore;

    [HttpGet("versions")]
    public async Task<IActionResult> Versions([FromQuery] string? platform, CancellationToken ct)
    {
        if (Store is null) return Ok(Array.Empty<object>());
        var rows = await Store.ListAsync(platform, ct);
        return Ok(rows.Select(r => new { r.Platform, r.Version, r.Package, r.ProtoSha, r.DetectedAt }));
    }

    [HttpGet("versions/{platform}/{version}")]
    public async Task<IActionResult> Get(string platform, string version, CancellationToken ct)
    {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, version, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return Ok(new { row.Platform, row.Version, row.Package, row.ProtoSha, row.DetectedAt,
            messages = pp is null ? "[]" : pp.MessageIndex, hasProto = pp is not null });
    }

    [HttpGet("versions/{platform}/{version}/proto")]
    public async Task<IActionResult> Proto(string platform, string version, CancellationToken ct)
    {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, version, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        if (pp is null) return NotFound();
        return Content(pp.ProtoText, "text/plain");
    }

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string platform = "android", CancellationToken ct = default)
    {
        if (Store is null) return NotFound();
        var rows = await Store.ListAsync(platform, ct);
        var r = rows.FirstOrDefault();
        return r is null ? NotFound() : Ok(new { r.Platform, r.Version, r.ProtoSha, r.DetectedAt });
    }

    // TODO(phase 3): GET diff?from=&to= computes message/field add+remove between two versions'
    // .proto texts server-side. Deferred; not stubbed so callers don't depend on a fake shape.
}
