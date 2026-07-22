using System.Globalization;

namespace EggIncognito.Services.ProtoExtract;


public static class ProtoVersionQuality {

    public static bool IsAndroidStyleBuild(string? build) =>
        !string.IsNullOrWhiteSpace(build) && build.All(char.IsDigit);



    public static bool HasPlatformBuildMismatch(string? platform, string? build) {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(build)) return false;
        var isIos = platform.Equals("ios", StringComparison.OrdinalIgnoreCase);
        return isIos && IsAndroidStyleBuild(build);
    }


    public static string? BuildQualityFlag(string? platform, string? build) =>
        HasPlatformBuildMismatch(platform, build) ? "android_build_on_ios" : null;



    public static long LatestSortKey(string? platform, string? build, string? appVersion) {
        if (string.IsNullOrWhiteSpace(platform)) return long.MinValue;
        if (platform.Equals("android", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vc) ? vc : long.MinValue;


        return HasPlatformBuildMismatch(platform, build) ? long.MinValue : DottedVersionKey(appVersion);
    }


    internal static long DottedVersionKey(string? version) {
        if (string.IsNullOrWhiteSpace(version)) return long.MinValue;
        var parts = version.Split('.');
        long key = 0;
        for (var i = 0; i < 4; i++) {
            long comp = 0;
            if (i < parts.Length && long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p >= 0) comp = Math.Min(p, 0xFFFF);
            key = (key << 16) | comp;
        }
        return key;
    }
}
