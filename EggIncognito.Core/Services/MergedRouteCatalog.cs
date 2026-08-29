namespace EggIncognito.Core.Services;

public interface IDbRouteProvider {
    RouteInfo? GetDbRoute(string path);
    IReadOnlyList<RouteInfo> AllDbRoutes();
    void Invalidate();
}

public sealed class MergedRouteCatalog(RouteCatalog yaml, IDbRouteProvider? db, IBinaryRouteProvider? binary = null)
    : IRouteCatalog {
    public RouteInfo? Resolve(string path) {
        if (yaml.Resolve(path) is { } y) return y;
        if (db?.GetDbRoute(path) is { } d) return d;
        var b = binary?.GetBinaryRoute(path);
        return b is null ? null : ToRouteInfo(b);
    }

    public IReadOnlyList<RouteInfo> All() {
        var yamlRoutes = yaml.All();
        var known = new HashSet<string>(yamlRoutes.Select(r => r.Path), StringComparer.Ordinal);
        var result = new List<RouteInfo>(yamlRoutes);

        if (db is not null) {
            foreach (var r in db.AllDbRoutes()) {
                if (known.Add(r.Path)) result.Add(r);
            }
        }

        if (binary is not null) {
            foreach (var b in binary.AllBinaryRoutes()) {
                if (known.Add(b.Path)) result.Add(ToRouteInfo(b));
            }
        }

        return result;
    }

    private static RouteInfo ToRouteInfo(BinaryRouteInfo b) =>
        new(b.Path, b.Request, b.Response, b.RequestWrapped, b.ResponseWrapped, null, false, false);
}
