namespace EggIncognito.Core.Services.Devices;

public static class AdbHostKey {
    public const string FileName = "adbkey.pub";
    public const string RootHomeKey = "/root/.android/" + FileName;

    public static string? Resolve(VirtualDeviceConfig config) =>
        Resolve(config, Environment.GetEnvironmentVariable("ANDROID_USER_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    public static string? Resolve(VirtualDeviceConfig config, string? androidUserHome, string? userProfile) {
        if (Nz(config.AdbPublicKey) is { } literal) return literal;
        foreach (string path in Candidates(config.AdbPublicKeyPath, androidUserHome, userProfile)) {
            if (ReadKey(path) is { } key) return key;
        }

        return null;
    }

    public static IEnumerable<string> Candidates(string? configuredPath, string? androidUserHome, string? userProfile) {
        if (Nz(configuredPath) is { } configured) yield return configured;
        if (Nz(androidUserHome) is { } home) yield return Path.Combine(home, FileName);
        if (Nz(userProfile) is { } profile) yield return Path.Combine(profile, ".android", FileName);
        yield return RootHomeKey;
    }

    private static string? ReadKey(string path) {
        try {
            return File.Exists(path) ? Nz(File.ReadAllText(path)) : null;
        } catch (IOException) {
            return null;
        } catch (UnauthorizedAccessException) {
            return null;
        }
    }

    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
