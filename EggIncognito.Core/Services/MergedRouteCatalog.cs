namespace EggIncognito.Services;

public interface IDbRouteProvider {
    RouteInfo? GetDbRoute(string path);
    IReadOnlyList<RouteInfo> AllDbRoutes();
}

public sealed class MergedRouteCatalog(RouteCatalog yaml, IDbRouteProvider? db) : IRouteCatalog {
    public RouteInfo? Get(string path) => yaml.Get(path) ?? db?.GetDbRoute(path);

    public IReadOnlyList<RouteInfo> All() {
        var yamlRoutes = yaml.All();
        if (db is null) return yamlRoutes;
        var known = new HashSet<string>(yamlRoutes.Select(r => r.Path), StringComparer.Ordinal);
        var extra = db.AllDbRoutes().Where(r => !known.Contains(r.Path));
        return yamlRoutes.Concat(extra).ToList();
    }
}
