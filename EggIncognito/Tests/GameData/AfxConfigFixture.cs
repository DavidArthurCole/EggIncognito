namespace EggIncognito.Tests.GameData;

internal static class AfxConfigFixture {
    private static string? _cache;
    private static bool _attempted;

    public static bool TryLoad(out string json) {
        if (!_attempted) {
            _cache = Locate();
            _attempted = true;
        }

        json = _cache ?? "";
        return _cache is not null;
    }

    private static string? Locate() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        if (dir is null) return null;

        string path = Path.Combine(dir.FullName, "EggIncognito", "captures", "fixtures", "ei_afx_config.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
