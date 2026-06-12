// Process-lifetime view of the drop-in API surface: the merged AuxbrainCatalog entries, the
// canonical real-API path registry, the known auxbrain namespaces, and the OpenAPI document.
// Every input (routes.yaml, auxbrain-paths.json, Endpoints/default status) is static per process,
// so each piece is built lazily once and cached. Backs ApiSurfaceController and the
// DynamicMockController namespace fallback.

namespace EggIncognito.Services;

public sealed class AuxbrainSurface
{
    private readonly Lazy<IReadOnlyDictionary<string, CanonicalPath>> _canonical;
    private readonly Lazy<IReadOnlyList<AuxbrainEntry>> _entries;
    private readonly Lazy<IReadOnlySet<string>> _namespaces;
    private readonly Lazy<string> _openApiJson;
    private readonly Lazy<IReadOnlyDictionary<string, RouteInfo>> _aliases;

    public AuxbrainSurface(RouteCatalog routes, IProtoReflection reflection, IConfiguration config)
    {
        _canonical = new(() => AuxbrainCatalog.LoadCanonical(AuxbrainCatalog.ResolveJsonPath(config)));
        _entries = new(() =>
        {
            // Same status inputs as ToolsController's endpoint-status: the content root's
            // routes.yaml classified against its Endpoints/default directory.
            var root = ContentRoot.Resolve(config["ContentRoot"]);
            var status = EndpointStatus.Classify(
                Path.Combine(root, "RouteMap", "routes.yaml"),
                Path.Combine(root, "Endpoints", "default"));
            return AuxbrainCatalog.Build(routes.All(), _canonical.Value, status);
        });
        _namespaces = new(() =>
            _entries.Value.Select(e => e.Namespace).ToHashSet(StringComparer.Ordinal));
        _openApiJson = new(() => OpenApiBuilder.BuildJson(_entries.Value, reflection));
        // Aliases live only in routes.yaml (DB routes carry none), so the map is process-static.
        _aliases = new(() =>
        {
            var map = new Dictionary<string, RouteInfo>(StringComparer.Ordinal);
            foreach (var r in routes.All())
                foreach (var a in r.Aliases)
                    map[a] = r;
            return map;
        });
    }

    /// <summary>The canonical route an alias path resolves to, or null if not an alias.</summary>
    public RouteInfo? ResolveAlias(string path) =>
        _aliases.Value.TryGetValue(path, out var r) ? r : null;

    public IReadOnlyDictionary<string, CanonicalPath> Canonical => _canonical.Value;
    public IReadOnlyList<AuxbrainEntry> Entries => _entries.Value;
    public IReadOnlySet<string> Namespaces => _namespaces.Value;
    public string OpenApiJson => _openApiJson.Value;

    public bool IsKnownNamespace(string path)
    {
        var i = path.IndexOf('/');
        var ns = i < 0 ? path : path[..i];
        return Namespaces.Contains(ns);
    }
}
