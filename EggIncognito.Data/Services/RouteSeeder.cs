using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Mirrors the compiled yaml route catalog into stored_routes (source = "yaml") on boot: insert
// missing, update drifted type/flag columns, never touch source = "db" rows. Idempotent. The pure
// helpers (ToYamlRow / NeedsUpdate / Apply) are unit-tested; SeedAsync does the DB pass.
public static class RouteSeeder
{
    public static StoredRoute ToYamlRow(RouteInfo r) => new()
    {
        Path = r.Path,
        RequestType = r.Request,
        ResponseType = r.Response,
        RequestWrapped = r.RequestWrapped,
        ResponseWrapped = r.ResponseWrapped,
        RawResponse = r.RawResponse,
        PathParam = r.PathParam,
        PathParamOnly = r.PathParamOnly,
        Source = "yaml",
    };

    public static bool NeedsUpdate(StoredRoute existing, RouteInfo r)
        => existing.RequestType != r.Request
        || existing.ResponseType != r.Response
        || existing.RequestWrapped != r.RequestWrapped
        || existing.ResponseWrapped != r.ResponseWrapped
        || existing.RawResponse != r.RawResponse
        || existing.PathParam != r.PathParam
        || existing.PathParamOnly != r.PathParamOnly;

    public static void Apply(StoredRoute row, RouteInfo r)
    {
        row.RequestType = r.Request;
        row.ResponseType = r.Response;
        row.RequestWrapped = r.RequestWrapped;
        row.ResponseWrapped = r.ResponseWrapped;
        row.RawResponse = r.RawResponse;
        row.PathParam = r.PathParam;
        row.PathParamOnly = r.PathParamOnly;
    }

    public static async Task SeedAsync(EggIncognitoDbContext db, IRouteCatalog yamlCatalog, CancellationToken ct = default)
    {
        var existing = await db.StoredRoutes
            .Where(r => r.Source == "yaml")
            .ToDictionaryAsync(r => r.Path, ct);

        foreach (var info in yamlCatalog.All())
        {
            if (existing.TryGetValue(info.Path, out var row))
            {
                if (NeedsUpdate(row, info)) Apply(row, info);
            }
            else
            {
                db.StoredRoutes.Add(ToYamlRow(info));
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
