using EggIncognito.Data.Services;
using EggIncognito.Services.ProtoExtract;
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
        // Id + CanonicalId let the UI group a cross-platform release (canonical + its aliases) into one row.
        // buildFlag is COMPUTED (no DB column): flags rows whose build doesn't match their platform, e.g. an
        // iOS row carrying an Android-style integer versionCode (the shared wire build leaking in).
        return Ok(rows.Select(r => new
        {
            r.Id, r.CanonicalId, r.Platform, r.AppVersion, r.Build, r.ClientVersion, r.Source, r.Package,
            r.ProtoSha, r.DetectedAt,
            buildFlag = ProtoVersionQuality.BuildQualityFlag(r.Platform, r.Build),
        }));
    }

    [HttpGet("versions/{platform}/{build}")]
    public async Task<IActionResult> Get(string platform, string build, CancellationToken ct)
    {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return Ok(new { row.Platform, row.AppVersion, row.Build, row.ClientVersion, row.Source, row.Package,
            row.ProtoSha, row.DetectedAt,
            messages = pp is null ? "[]" : pp.MessageIndex, hasProto = pp is not null });
    }

    [HttpGet("versions/{platform}/{build}/proto")]
    public async Task<IActionResult> Proto(string platform, string build, CancellationToken ct)
    {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        if (pp is null) return NotFound();
        return Content(pp.ProtoText, "text/plain");
    }

    // Row count per source for the /protos/sources attribution page. No DB => empty object, so the page
    // renders its static credits with zero counts rather than erroring.
    [HttpGet("sources")]
    public async Task<IActionResult> Sources(CancellationToken ct)
    {
        if (Store is null) return Ok(new Dictionary<string, int>());
        return Ok(await Store.SourceCountsAsync(ct));
    }

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string platform = "android", CancellationToken ct = default)
    {
        if (Store is null) return NotFound();
        var rows = await Store.ListAsync(platform, ct);
        // "Latest" = newest release, NOT the most-recently-inserted row (backfill ingests history in arbitrary
        // order, so CreatedAt is not a recency proxy). Platform-aware: Android orders by integer versionCode;
        // iOS orders by dotted CFBundleVersion and refuses to let a bad Android-style integer build win.
        var r = rows
            .OrderByDescending(p => ProtoVersionQuality.LatestSortKey(p.Platform, p.Build, p.AppVersion))
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        return r is null ? NotFound()
            : Ok(new { r.Platform, r.AppVersion, r.Build, r.ClientVersion, r.Source, r.ProtoSha, r.DetectedAt });
    }

    // Namespace-insensitive diff between two stored versions' .proto texts (port of protodiff.py).
    // 404 if either version or its proto text is missing; text/plain `@@ message @@` +/- sections.
    [HttpGet("diff")]
    public async Task<IActionResult> Diff(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] string platform = "android",
        CancellationToken ct = default)
    {
        if (Store is null) return NotFound();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "from and to required" });

        var fromText = await LoadProtoText(platform, from, ct);
        var toText = await LoadProtoText(platform, to, ct);
        if (fromText is null || toText is null) return NotFound();

        return Content(ProtoDiff.Diff(fromText, toText), "text/plain");
    }

    private async Task<string?> LoadProtoText(string platform, string build, CancellationToken ct)
    {
        var row = await Store!.GetAsync(platform, build, ct);
        if (row is null) return null;
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return pp?.ProtoText;
    }
}
