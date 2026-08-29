namespace EggIncognito.Core.Services;

public sealed record BinaryRouteInfo(
    string Path,
    string? Method,
    string? Request,
    string? Response,
    bool RequestWrapped,
    bool ResponseWrapped,
    string? BinaryVersion,
    string? Platform,
    DateTimeOffset RefreshedAt);

public interface IBinaryRouteProvider {
    BinaryRouteInfo? GetBinaryRoute(string path);
    IReadOnlyList<BinaryRouteInfo> AllBinaryRoutes();
    void Invalidate();
}

public sealed record RouteDriftRow(string Path, string Field, string? EffectiveValue, string? BinaryValue, bool Reliable);

public static class RouteDrift {
    public static IReadOnlyList<RouteDriftRow> Compute(IEnumerable<RouteInfo> effective,
        IEnumerable<BinaryRouteInfo> binary) {
        var effectiveByPath = effective.ToDictionary(r => r.Path, StringComparer.Ordinal);
        var rows = new List<RouteDriftRow>();

        foreach (var b in binary) {
            if (!effectiveByPath.TryGetValue(b.Path, out var e)) {
                rows.Add(new RouteDriftRow(b.Path, "new", null, "binary-only endpoint", false));
                continue;
            }

            if (e.RequestWrapped != b.RequestWrapped) {
                rows.Add(new RouteDriftRow(b.Path, "requestWrapped", e.RequestWrapped.ToString(),
                    b.RequestWrapped.ToString(), true));
            }

            if (e.ResponseWrapped != b.ResponseWrapped) {
                rows.Add(new RouteDriftRow(b.Path, "responseWrapped", e.ResponseWrapped.ToString(),
                    b.ResponseWrapped.ToString(), false));
            }
        }

        return rows;
    }
}
