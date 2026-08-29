namespace EggIncognito.Core.Services;

public sealed class OverlayRouteCatalog(IRouteCatalog inner, IRouteOverrideProvider? overrides) : IRouteCatalog {
    public RouteInfo? Resolve(string path) {
        var route = inner.Resolve(path);
        if (route is null || overrides is null) return route;
        var snapshot = overrides.Snapshot();
        return snapshot.TryGetValue(route.Path, out var o) ? Merge(route, o) : route;
    }

    public IReadOnlyList<RouteInfo> All() {
        if (overrides is null) return inner.All();
        var snapshot = overrides.Snapshot();
        return inner.All().Select(r => snapshot.TryGetValue(r.Path, out var o) ? Merge(r, o) : r).ToList();
    }

    private static RouteInfo Merge(RouteInfo r, RouteOverrideInfo o) =>
        r with {
            Request = o.Request ?? r.Request,
            Response = o.Response ?? r.Response,
            RequestWrapped = o.RequestWrapped ?? r.RequestWrapped,
            ResponseWrapped = o.ResponseWrapped ?? r.ResponseWrapped,
            PathParam = o.PathParam ?? r.PathParam
        };
}
