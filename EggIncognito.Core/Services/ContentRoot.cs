namespace EggIncognito.Services;

// The directory that holds RouteMap/routes.yaml + Endpoints/ (+ writable captures/). Resolved
// app-relative so it works hosted (next to the published payload) and locally, with a config
// override. Replaces the old .slnx repo-root walk - no dev-tree assumption.
public static class ContentRoot
{
    // configured = the ContentRoot config value (or null). Returns the first plausible directory:
    // the configured path, else the app base dir if it contains RouteMap/, else a base-dir ancestor
    // that does (dev run from bin/), else the base dir.
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
}
