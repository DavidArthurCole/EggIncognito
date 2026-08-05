namespace EggIncognito.Services;

public enum AuxbrainStatus {
    Ok,
    Empty,
    Missing
}

public sealed record AuxbrainEntry(
    string Path,
    string Namespace,
    string? RequestType,
    string? ResponseType,
    bool RequestWrapped,
    bool ResponseWrapped,
    bool PathParam,
    AuxbrainStatus Status) {
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public static class AuxbrainCatalog {
    public static string Label(AuxbrainStatus status) => status switch {
        AuxbrainStatus.Ok => "ok",
        AuxbrainStatus.Empty => "empty",
        AuxbrainStatus.Missing => "missing",
        _ => "missing"
    };

    public static IReadOnlyList<AuxbrainEntry> Build(
        IReadOnlyList<RouteInfo> routes,
        EndpointStatus.Result status) {
        var empty = status.Empty.ToHashSet(StringComparer.Ordinal);
        var missing = status.Missing.ToHashSet(StringComparer.Ordinal);
        var entries = new List<AuxbrainEntry>();

        foreach (var r in routes) {
            var s = empty.Contains(r.Path) ? AuxbrainStatus.Empty
                : missing.Contains(r.Path) ? AuxbrainStatus.Missing
                : AuxbrainStatus.Ok;
            entries.Add(new AuxbrainEntry(
                r.Path, NamespaceOf(r.Path), r.Request, r.Response,
                r.RequestWrapped, r.ResponseWrapped, r.PathParam, s) { Aliases = r.Aliases });
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }


    private static string NamespaceOf(string path) {
        int i = path.IndexOf('/');
        return i < 0 ? path : path[..i];
    }
}
