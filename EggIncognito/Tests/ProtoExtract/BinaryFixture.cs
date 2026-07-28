namespace EggIncognito.Tests.ProtoExtract;

internal static class BinaryFixture {
    private static byte[]? _cache;
    private static bool _attempted;

    public static bool TryLoad(out byte[] bin) {
        if (!_attempted) {
            _cache = Locate();
            _attempted = true;
        }

        bin = _cache ?? [];
        return _cache is not null;
    }

    private static byte[]? Locate() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EggIncognito.slnx"))) dir = dir.Parent;
        if (dir is null) return null;

        string path = Path.Combine(dir.FullName, "EggIncognito", "captures", "egginc");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 1_000_000) return null;
        return File.ReadAllBytes(path);
    }
}
