namespace EggIncognito.Core.Services.Devices;

public static class Platforms {
    public const string Ios = "ios";
    public const string Android = "android";

    public static bool Matches(string? value, string platform) =>
        string.Equals(value, platform, StringComparison.OrdinalIgnoreCase);
}
