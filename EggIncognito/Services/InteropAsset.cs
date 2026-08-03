namespace EggIncognito.Services;

public static class InteropAsset {
    public static string Url(IWebHostEnvironment env, string path) {
        try {
            var relative = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
            var fi = env.WebRootFileProvider.GetFileInfo(relative);
            if (fi.Exists) return $"{path}?v={fi.LastModified.UtcTicks}";
        } catch {
        }

        return path;
    }
}
