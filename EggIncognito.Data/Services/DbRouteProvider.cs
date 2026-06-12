using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

// Surfaces source = "db" stored_routes as RouteInfo for the merged catalog, new paths only; yaml
// routes are served by the generated controllers and never come from here. Scoped on the DbContext.
public sealed class DbRouteProvider(EggIncognitoDbContext db, ILogger<DbRouteProvider> logger) : IDbRouteProvider
{
    public RouteInfo? GetDbRoute(string path)
    {
        try
        {
            var r = db.StoredRoutes.AsNoTracking().FirstOrDefault(x => x.Path == path && x.Source == "db");
            return r is null ? null : ToInfo(r);
        }
        catch (Exception ex)
        {
            // A transient DB error must not fail the request - fall back to yaml-only routing.
            logger.LogWarning(ex, "DB route lookup failed for {Path}; using yaml-only routing", path);
            return null;
        }
    }

    public IReadOnlyList<RouteInfo> AllDbRoutes()
    {
        try
        {
            return db.StoredRoutes.AsNoTracking().Where(x => x.Source == "db").Select(ToInfo).ToList();
        }
        catch (Exception ex)
        {
            // A transient DB error must not fail the request - fall back to yaml-only routing.
            logger.LogWarning(ex, "DB route listing failed; using yaml-only routing");
            return [];
        }
    }

    private static RouteInfo ToInfo(Models.StoredRoute r) => new(
        r.Path, r.RequestType, r.ResponseType, r.RequestWrapped, r.ResponseWrapped,
        r.RawResponse, r.PathParam, r.PathParamOnly);
}
