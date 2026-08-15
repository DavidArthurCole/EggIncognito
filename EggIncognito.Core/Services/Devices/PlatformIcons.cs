namespace EggIncognito.Core.Services.Devices;

public static class PlatformIcons {
    public static string For(string? platform) {
        if (Platforms.Matches(platform, Platforms.Ios)) return "apple";
        if (Platforms.Matches(platform, Platforms.Android)) return "android";
        return "device";
    }
}
