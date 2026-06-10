namespace EggIncognito.Services;

// Supplies DB-sourced routes (source = "db", new paths only) to the merged catalog. Implemented in
// EggIncognito.Data; null when no database is configured.
public interface IDbRouteProvider
{
    RouteInfo? GetDbRoute(string path);
    IReadOnlyList<RouteInfo> AllDbRoutes();
}

// Merges the authoritative yaml catalog with DB-only routes. yaml always wins for a path it defines;
// the DB provider only fills paths yaml lacks. With a null provider this is exactly the yaml catalog.
public sealed class MergedRouteCatalog(RouteCatalog yaml, IDbRouteProvider? db) : IRouteCatalog
{
    public RouteInfo? Get(string path) => yaml.Get(path) ?? db?.GetDbRoute(path);

    public IReadOnlyList<RouteInfo> All()
    {
        var yamlRoutes = yaml.All();
        if (db is null) return yamlRoutes;
        var known = new HashSet<string>(yamlRoutes.Select(r => r.Path), StringComparer.Ordinal);
        var extra = db.AllDbRoutes().Where(r => !known.Contains(r.Path));
        return yamlRoutes.Concat(extra).ToList();
    }
}
