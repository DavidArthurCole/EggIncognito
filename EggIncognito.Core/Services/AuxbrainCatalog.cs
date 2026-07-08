// Merged view of the mock surface (routes.yaml via RouteCatalog) and the canonical real-API path
// registry (RouteMap/auxbrain-paths.json). One entry per path in the union: mocked paths keep the
// route's shape and carry their EndpointStatus bucket; canonical-only paths are real-but-unmocked and
// take their shape from the registry. Pure: parsed inputs in, entries out, so tests need no disk.

using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services;

public enum AuxbrainStatus { Ok, Empty, Missing, NotMocked }

// One auxbrain-paths.json value: the real API's shape for a path.
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
    AuxbrainStatus Status)
{
    /// <summary>Alternate request paths that resolve to this route. Empty for canonical-only entries.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public static class AuxbrainCatalog
{
    // Wire/JSON form of a status, used by the OpenAPI vendor extension and the catalog API.
    public static string Label(AuxbrainStatus status) => status switch
    {
        AuxbrainStatus.Ok => "ok",
        AuxbrainStatus.Empty => "empty",
        AuxbrainStatus.Missing => "missing",
        _ => "not-mocked",
    };

    public static IReadOnlyList<AuxbrainEntry> Build(
        IReadOnlyList<RouteInfo> routes,
        IReadOnlyDictionary<string, CanonicalPath> canonical,
        EndpointStatus.Result status)
    {
        var empty = status.Empty.ToHashSet(StringComparer.Ordinal);
        var missing = status.Missing.ToHashSet(StringComparer.Ordinal);
        var entries = new List<AuxbrainEntry>();

        // A path is mocked if it is a route or resolves to one via alias (the catch-all serves
        // aliases), so an aliased canonical key never shows up as a separate not-mocked entry.
        var mocked = routes.Select(r => r.Path)
            .Concat(routes.SelectMany(r => r.Aliases))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var r in routes)
        {
            var s = empty.Contains(r.Path) ? AuxbrainStatus.Empty
                : missing.Contains(r.Path) ? AuxbrainStatus.Missing
                : AuxbrainStatus.Ok; // raw-response routes are skipped by Classify and serve literals
            entries.Add(new AuxbrainEntry(
                r.Path, NamespaceOf(r.Path), r.Request, r.Response,
                r.RequestWrapped, r.ResponseWrapped, r.PathParam, s)
            { Aliases = r.Aliases });
        }

        foreach (var (path, c) in canonical)
        {
            if (mocked.Contains(path)) continue;
            entries.Add(new AuxbrainEntry(
                path, NamespaceOf(path), c.RequestType, c.ResponseType,
                c.RequestWrapped, c.ResponseWrapped, c.PathParam, AuxbrainStatus.NotMocked));
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }

    // First path segment: "ei_ctx/get_leaderboard" -> "ei_ctx".
    private static string NamespaceOf(string path)
    {
        var i = path.IndexOf('/');
        return i < 0 ? path : path[..i];
    }

    public static IReadOnlyDictionary<string, CanonicalPath> LoadCanonical(string jsonPath)
    {
        var result = new Dictionary<string, CanonicalPath>(StringComparer.Ordinal);
        if (!File.Exists(jsonPath)) return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
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

    // Same resolution as RouteCatalog's routes.yaml: config override, then search up from the app base dir.
    public static string ResolveJsonPath(IConfiguration config) =>
        ContentRoot.ResolveRouteMapFile(config["AuxbrainPathsPath"], "auxbrain-paths.json");
}
