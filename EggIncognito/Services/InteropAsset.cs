namespace EggIncognito.Services;

public static class InteropAsset {
    public static string Url(IWebHostEnvironment env, string path) {
        string relative = path.StartsWith("./", StringComparison.Ordinal) ? path[2..] : path;
        var fi = env.WebRootFileProvider.GetFileInfo(relative);
        return fi.Exists ? $"{path}?v={fi.LastModified.UtcTicks}" : path;
    }
}
