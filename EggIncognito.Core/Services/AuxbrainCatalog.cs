using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services;

public enum AuxbrainStatus {
    Ok,
    Empty,
    Missing,
    NotMocked
}

public sealed record CanonicalPath(
    string? RequestType,
    string? ResponseType,
    bool RequestWrapped,
    bool ResponseWrapped,
    bool PathParam);

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
        _ => "not-mocked"
    };

    public static IReadOnlyList<AuxbrainEntry> Build(
        IReadOnlyList<RouteInfo> routes,
        IReadOnlyDictionary<string, CanonicalPath> canonical,
        EndpointStatus.Result status) {
        var empty = status.Empty.ToHashSet(StringComparer.Ordinal);
        var missing = status.Missing.ToHashSet(StringComparer.Ordinal);
        var entries = new List<AuxbrainEntry>();


        var mocked = routes.Select(r => r.Path)
            .Concat(routes.SelectMany(r => r.Aliases))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var r in routes) {
            var s = empty.Contains(r.Path) ? AuxbrainStatus.Empty
                : missing.Contains(r.Path) ? AuxbrainStatus.Missing
                : AuxbrainStatus.Ok;
            entries.Add(new AuxbrainEntry(
                r.Path, NamespaceOf(r.Path), r.Request, r.Response,
                r.RequestWrapped, r.ResponseWrapped, r.PathParam, s) { Aliases = r.Aliases });
        }

        foreach ((string path, var c) in canonical) {
            if (mocked.Contains(path)) continue;
            entries.Add(new AuxbrainEntry(
                path, NamespaceOf(path), c.RequestType, c.ResponseType,
                c.RequestWrapped, c.ResponseWrapped, c.PathParam, AuxbrainStatus.NotMocked));
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }


    private static string NamespaceOf(string path) {
        int i = path.IndexOf('/');
        return i < 0 ? path : path[..i];
    }

    public static IReadOnlyDictionary<string, CanonicalPath> LoadCanonical(string jsonPath) {
        var result = new Dictionary<string, CanonicalPath>(StringComparer.Ordinal);
        if (!File.Exists(jsonPath)) return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        foreach (var p in doc.RootElement.EnumerateObject()) {
            result[p.Name] = new CanonicalPath(
                Str(p.Value, "requestType"),
                Str(p.Value, "responseType"),
                Flag(p.Value, "requestWrapped"),
                Flag(p.Value, "responseWrapped"),
                Flag(p.Value, "pathParam"));
        }

        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Flag(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;


    public static string ResolveJsonPath(IConfiguration config) =>
        ContentRoot.ResolveRouteMapFile(config["AuxbrainPathsPath"], "auxbrain-paths.json");
}
