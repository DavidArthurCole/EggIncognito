using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

// Surfaces source = "db" stored_routes as RouteInfo for the merged catalog (new paths only; yaml
// routes are served by the generated controllers and never come from here). Scoped on the DbContext.
public sealed class DbRouteProvider(EggIncognitoDbContext db) : IDbRouteProvider
{
    public RouteInfo? GetDbRoute(string path)
    {
        var r = db.StoredRoutes.AsNoTracking().FirstOrDefault(x => x.Path == path && x.Source == "db");
        return r is null ? null : ToInfo(r);
    }

    public IReadOnlyList<RouteInfo> AllDbRoutes()
        => db.StoredRoutes.AsNoTracking().Where(x => x.Source == "db").Select(ToInfo).ToList();

    private static RouteInfo ToInfo(Models.StoredRoute r) => new(
        r.Path, r.RequestType, r.ResponseType, r.RequestWrapped, r.ResponseWrapped,
        r.RawResponse, r.PathParam, r.PathParamOnly);
}
