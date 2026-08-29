using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Routes;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin/routes")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed class RouteAdminController(
    IRouteCatalog routes,
    RouteCatalog yamlRoutes,
    IProtoReflection proto,
    ICurrentUser currentUser,
    IServiceProvider services) : ControllerBase {
    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;

    private IRouteOverrideProvider? Overrides =>
        services.GetService(typeof(IRouteOverrideProvider)) as IRouteOverrideProvider;

    private IBinaryRouteProvider? Binary =>
        services.GetService(typeof(IBinaryRouteProvider)) as IBinaryRouteProvider;

    private IDbRouteProvider? DbRoutes =>
        services.GetService(typeof(IDbRouteProvider)) as IDbRouteProvider;

    [HttpGet]
    [EnableRateLimiting("read")]
    public IActionResult List() {
        var overrides = Overrides?.Snapshot() ?? new Dictionary<string, RouteOverrideInfo>(StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<object>();

        foreach (var route in routes.All()) {
            overrides.TryGetValue(route.Path, out var o);
            if (o is not null) matched.Add(route.Path);
            rows.Add(new {
                path = route.Path,
                source = yamlRoutes.Resolve(route.Path) is not null ? "yaml" : "db",
                effective = EffectiveOf(route),
                @override = OverrideOf(o)
            });
        }

        foreach (var o in overrides.Values) {
            if (matched.Contains(o.Path)) continue;
            rows.Add(new {
                path = o.Path,
                source = "orphan",
                effective = (object?)null,
                @override = OverrideOf(o)
            });
        }

        return Ok(rows);
    }

    [HttpGet("binary")]
    [EnableRateLimiting("read")]
    public IActionResult ListBinary() {
        var binary = Binary;
        if (binary is null) return StatusCode(503, new { error = "no database configured" });

        var rows = binary.AllBinaryRoutes();
        var nonBinaryEffective = new OverlayRouteCatalog(new MergedRouteCatalog(yamlRoutes, DbRoutes), Overrides).All();
        var edited = new HashSet<string>(
            Overrides?.Snapshot().Keys ?? [], StringComparer.Ordinal);
        var drift = RouteDrift.Compute(nonBinaryEffective, rows)
            .Where(d => d.Field == "new" || !edited.Contains(d.Path))
            .ToList();
        DateTimeOffset? lastRefresh = rows.Count == 0 ? null : rows.Max(r => r.RefreshedAt);
        string? binaryVersion = ProvenanceOf(rows);

        return Ok(new {
            lastRefresh,
            binaryVersion,
            discovered = rows.Count,
            newCount = drift.Count(d => d.Field == "new"),
            driftCount = drift.Count(d => d.Field != "new"),
            rows = rows.Select(BinaryRowOf),
            drift = drift.Select(DriftRowOf)
        });
    }

    [HttpPost("binary/refresh")]
    public async Task<IActionResult> RefreshBinaryAsync(CancellationToken ct) {
        if (Db is null) return StatusCode(503, new { error = "no database configured" });
        if (services.GetService(typeof(EndpointCatalogRebuilder)) is not EndpointCatalogRebuilder rebuilder)
            return StatusCode(503, new { error = "no database configured" });

        return Ok(await rebuilder.RebuildAsync(ct));
    }

    [HttpPut("{**path}")]
    public async Task<IActionResult> UpsertAsync(string path, [FromBody] UpsertRouteOverride body) {
        if (routes.Resolve(path) is null) return NotFound(new { error = $"unknown route {path}" });
        if (body.Request is null && body.Response is null && body.RequestWrapped is null
            && body.ResponseWrapped is null && body.PathParam is null) {
            return BadRequest(new { error = "all fields null, use DELETE to remove an override" });
        }

        if (body.Request is not null && proto.FindMessage(body.Request) is null)
            return BadRequest(new { error = $"unknown proto type {body.Request}" });
        if (body.Response is not null && proto.FindMessage(body.Response) is null)
            return BadRequest(new { error = $"unknown proto type {body.Response}" });

        var db = Db;
        var provider = Overrides;
        if (db is null || provider is null) return StatusCode(503, new { error = "no database configured" });

        var now = DateTimeOffset.UtcNow;
        var existing = await db.RouteOverrides.FirstOrDefaultAsync(o => o.Path == path);
        if (existing is null) {
            db.RouteOverrides.Add(new RouteOverride {
                Path = path,
                RequestType = body.Request,
                ResponseType = body.Response,
                RequestWrapped = body.RequestWrapped,
                ResponseWrapped = body.ResponseWrapped,
                PathParam = body.PathParam,
                UpdatedAt = now,
                UpdatedBy = currentUser.UserId
            });
        } else {
            existing.RequestType = body.Request;
            existing.ResponseType = body.Response;
            existing.RequestWrapped = body.RequestWrapped;
            existing.ResponseWrapped = body.ResponseWrapped;
            existing.PathParam = body.PathParam;
            existing.UpdatedAt = now;
            existing.UpdatedBy = currentUser.UserId;
        }

        await db.SaveChangesAsync();
        provider.Invalidate();

        var effective = routes.Resolve(path)!;
        return Ok(new { path = effective.Path, effective = EffectiveOf(effective) });
    }

    [HttpDelete("{**path}")]
    public async Task<IActionResult> DeleteAsync(string path) {
        var db = Db;
        var provider = Overrides;
        if (db is null || provider is null) return StatusCode(503, new { error = "no database configured" });

        var existing = await db.RouteOverrides.FirstOrDefaultAsync(o => o.Path == path);
        if (existing is null) return NotFound(new { error = $"no override for {path}" });
        db.RouteOverrides.Remove(existing);
        await db.SaveChangesAsync();
        provider.Invalidate();
        return Ok(new { deleted = path });
    }

    private static object EffectiveOf(RouteInfo r) => new {
        request = r.Request,
        response = r.Response,
        requestWrapped = r.RequestWrapped,
        responseWrapped = r.ResponseWrapped,
        pathParam = r.PathParam
    };

    private static object BinaryRowOf(BinaryRouteInfo b) => new {
        path = b.Path,
        method = b.Method,
        request = b.Request,
        response = b.Response,
        requestWrapped = b.RequestWrapped,
        responseWrapped = b.ResponseWrapped,
        binaryVersion = b.BinaryVersion,
        platform = b.Platform,
        refreshedAt = b.RefreshedAt
    };

    private static string? ProvenanceOf(IReadOnlyList<BinaryRouteInfo> rows) {
        var pairs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.BinaryVersion))
            .Select(r => (Platform: r.Platform ?? "", Version: r.BinaryVersion!))
            .Distinct()
            .ToList();
        if (pairs.Count == 0) return null;

        pairs.Sort((a, b) => {
            int cmp = DeviceParsing.CompareVersions(b.Version, a.Version);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Platform, b.Platform);
        });
        return string.Join(" + ",
            pairs.Select(p => p.Platform.Length == 0 ? p.Version : $"{p.Platform} {p.Version}"));
    }

    private static object DriftRowOf(RouteDriftRow d) => new {
        path = d.Path,
        field = d.Field,
        effectiveValue = d.EffectiveValue,
        binaryValue = d.BinaryValue,
        reliable = d.Reliable
    };

    private static object? OverrideOf(RouteOverrideInfo? o) => o is null
        ? null
        : new {
            request = o.Request,
            response = o.Response,
            requestWrapped = o.RequestWrapped,
            responseWrapped = o.ResponseWrapped,
            pathParam = o.PathParam,
            updatedAt = o.UpdatedAt,
            updatedBy = o.UpdatedBy
        };
}
