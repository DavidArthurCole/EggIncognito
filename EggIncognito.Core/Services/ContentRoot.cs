namespace EggIncognito.Services;

// The directory that holds RouteMap/routes.yaml + Endpoints/ + writable captures/. Resolved
// app-relative so it works hosted next to the published payload and locally, with a config override.
// No dev-tree assumption.
public static class ContentRoot
{
    // configured = the ContentRoot config value, or null. Returns the first plausible directory: the
    // configured path, else the app base dir if it contains RouteMap/, else a base-dir ancestor that
    // does, else the base dir.
    public static string Resolve(string? configured)
    {
        if (!string.IsNullOrEmpty(configured)) return configured;

        var baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "RouteMap"))) return baseDir;

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "RouteMap"))) return dir.FullName;
            var nested = Path.Combine(dir.FullName, "EggIncognito");
            if (Directory.Exists(Path.Combine(nested, "RouteMap"))) return nested;
            dir = dir.Parent;
        }
        return baseDir;
    }

    /// <summary>routes.yaml under a known content root. The one place that spells out the
    /// RouteMap/routes.yaml join.</summary>
    public static string RoutesYamlPath(string contentRoot) =>
        Path.Combine(contentRoot, "RouteMap", "routes.yaml");

    /// <summary>Resolve a RouteMap/ file with no known content root: an explicit configured path
    /// wins, else search up from the app base dir, plain or under an EggIncognito/ subdir, else
    /// fall back to the base-dir-relative path. Shared by RouteCatalog (routes.yaml) and
    /// AuxbrainCatalog (auxbrain-paths.json).</summary>
    public static string ResolveRouteMapFile(string? configured, string fileName)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "RouteMap", fileName);
            if (File.Exists(candidate)) return candidate;
            var nested = Path.Combine(dir.FullName, "EggIncognito", "RouteMap", fileName);
            if (File.Exists(nested)) return nested;
            dir = dir.Parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "RouteMap", fileName);
    }
}
