using EggIncognito.Data.Models;
using EggIncognito.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Data.Services;

public sealed class BinaryRouteProvider(EggIncognitoDbContext db, ILogger<BinaryRouteProvider> logger)
    : IBinaryRouteProvider {
    public BinaryRouteInfo? GetBinaryRoute(string path) {
        try {
            var r = db.RouteBinaryCatalogs.AsNoTracking().FirstOrDefault(x => x.Path == path);
            return r is null ? null : ToInfo(r);
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary route lookup failed for {Path}; ignoring binary tier", path);
            return null;
        }
    }

    public IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes() {
        try {
            return db.RouteBinaryCatalogs.AsNoTracking().Select(ToInfo).ToList();
        } catch (Exception ex) {
            logger.LogWarning(ex, "binary route listing failed; ignoring binary tier");
            return [];
        }
    }

    public void Invalidate() {
    }

    private static BinaryRouteInfo ToInfo(RouteBinaryCatalog r) => new(
        r.Path, r.Method, r.RequestType, r.ResponseType, r.RequestWrapped, r.ResponseWrapped,
        r.BinaryVersion, r.Platform, r.RefreshedAt);
}
