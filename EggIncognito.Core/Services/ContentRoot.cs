namespace EggIncognito.Core.Services;

public static class ContentRoot {
    public static string Resolve(string? configured) {
        if (!string.IsNullOrEmpty(configured)) return configured;

        string baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(Path.Combine(baseDir, "RouteMap"))) return baseDir;

        var dir = new DirectoryInfo(baseDir);
        while (dir is not null) {
            if (Directory.Exists(Path.Combine(dir.FullName, "RouteMap"))) return dir.FullName;
            string nested = Path.Combine(dir.FullName, "EggIncognito");
            if (Directory.Exists(Path.Combine(nested, "RouteMap"))) return nested;
            dir = dir.Parent;
        }

        return baseDir;
    }


    public static string RoutesYamlPath(string contentRoot) =>
        Path.Combine(contentRoot, "RouteMap", "routes.yaml");


    public static string ResolveRouteMapFile(string? configured, string fileName) {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            string candidate = Path.Combine(dir.FullName, "RouteMap", fileName);
            if (File.Exists(candidate)) return candidate;
            string nested = Path.Combine(dir.FullName, "EggIncognito", "RouteMap", fileName);
            if (File.Exists(nested)) return nested;
            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "RouteMap", fileName);
    }
}
