using EggIncognito.Data.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.ProtoExtract;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("fetch")]
public sealed class ProtosController(IServiceProvider services) : ControllerBase {
    private ProtoRegistryStore? Store =>
        services.GetService(typeof(ProtoRegistryStore)) as ProtoRegistryStore;

    [HttpGet("versions")]
    public async Task<IActionResult> Versions([FromQuery] string? platform, CancellationToken ct) {
        if (Store is not { } store) return Ok(Array.Empty<object>());
        var rows = await store.ListAsync(platform, ct);
        var orders = await store.ShaOrdersAsync(ct);

        return Ok(rows.Select(r => new {
            r.Id,
            r.CanonicalId,
            r.Platform,
            r.AppVersion,
            r.Build,
            r.ClientVersion,
            r.Source,
            r.Package,
            r.ProtoSha,
            r.DetectedAt,
            buildFlag = ProtoVersionQuality.BuildQualityFlag(r.Platform, r.Build),
            sortOrder = orders.TryGetValue(r.ProtoSha ?? "", out int so) ? so : 0
        }));
    }

    [HttpGet("versions/{platform}/{build}")]
    public async Task<IActionResult> Get(string platform, string build, CancellationToken ct) {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return Ok(new {
            row.Platform,
            row.AppVersion,
            row.Build,
            row.ClientVersion,
            row.Source,
            row.Package,
            row.ProtoSha,
            row.DetectedAt,
            messages = pp is null ? "[]" : pp.MessageIndex,
            hasProto = pp is not null
        });
    }

    [HttpGet("versions/{platform}/{build}/proto")]
    public async Task<IActionResult> Proto(string platform, string build, CancellationToken ct) {
        if (Store is null) return NotFound();
        var row = await Store.GetAsync(platform, build, ct);
        if (row is null) return NotFound();
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return pp is null ? NotFound() : Content(pp.ProtoText, "text/plain");
    }


    [HttpGet("sources")]
    public async Task<IActionResult> Sources(CancellationToken ct) =>
        Store is null ? Ok(new Dictionary<string, int>()) : Ok(await Store.SourceCountsAsync(ct));

    [HttpGet("latest")]
    public async Task<IActionResult> Latest([FromQuery] string platform = "android", CancellationToken ct = default) {
        if (Store is null) return NotFound();
        var rows = await Store.ListAsync(platform, ct);


        var r = rows
            .OrderByDescending(p => ProtoVersionQuality.LatestSortKey(p.Platform, p.Build, p.AppVersion))
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefault();
        return r is null
            ? NotFound()
            : Ok(new { r.Platform, r.AppVersion, r.Build, r.ClientVersion, r.Source, r.ProtoSha, r.DetectedAt });
    }


    [HttpGet("diff")]
    public async Task<IActionResult> Diff(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] string platform = "android",
        CancellationToken ct = default) {
        if (Store is null) return NotFound();
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest(new { error = "from and to required" });

        string? fromText = await LoadProtoText(platform, from, ct);
        string? toText = await LoadProtoText(platform, to, ct);
        return fromText is null || toText is null
            ? NotFound()
            : Content(ProtoDiff.Diff(fromText, toText), "text/plain");
    }

    private async Task<string?> LoadProtoText(string platform, string build, CancellationToken ct) {
        var row = await Store!.GetAsync(platform, build, ct);
        if (row is null) return null;
        var pp = await Store.GetProtoAsync(row.Id, ct);
        return pp?.ProtoText;
    }
}
