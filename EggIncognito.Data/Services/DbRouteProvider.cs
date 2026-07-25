using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public sealed class DbRouteProvider(EggIncognitoDbContext db, ILogger<DbRouteProvider> logger) : IDbRouteProvider {
    public RouteInfo? GetDbRoute(string path) {
        try {
            var r = db.StoredRoutes.AsNoTracking().FirstOrDefault(x => x.Path == path && x.Source == "db");
            return r is null ? null : ToInfo(r);
        } catch (Exception ex) {
            logger.LogWarning(ex, "DB route lookup failed for {Path}; using yaml-only routing", path);
            return null;
        }
    }

    public IReadOnlyList<RouteInfo> AllDbRoutes() {
        try {
            return db.StoredRoutes.AsNoTracking().Where(x => x.Source == "db").Select(ToInfo).ToList();
        } catch (Exception ex) {
            logger.LogWarning(ex, "DB route listing failed; using yaml-only routing");
            return [];
        }
    }

    private static RouteInfo ToInfo(StoredRoute r) => new(
        r.Path, r.RequestType, r.ResponseType, r.RequestWrapped, r.ResponseWrapped,
        r.RawResponse, r.PathParam, r.PathParamOnly);
}
