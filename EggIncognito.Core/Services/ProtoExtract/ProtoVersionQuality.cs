using System.Globalization;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ProtoVersionQuality {
    public static bool IsAndroidStyleBuild(string? build) =>
        !string.IsNullOrWhiteSpace(build) && build.All(char.IsDigit);

    public static bool HasPlatformBuildMismatch(string? platform, string? build) {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(build)) return false;
        bool isIos = platform.Equals("ios", StringComparison.OrdinalIgnoreCase);
        return isIos && IsAndroidStyleBuild(build);
    }

    public static string? BuildQualityFlag(string? platform, string? build) =>
        HasPlatformBuildMismatch(platform, build) ? "android_build_on_ios" : null;

    public const string FlawNoClientVersion = "no_client_version";
    public const string FlawBuildPlatformMismatch = "build_platform_mismatch";
    public const string FlawNoProto = "no_proto";

    public static IReadOnlyList<string> Flaws(
        string? platform, string? build, string? clientVersion,
        string? protoSha, bool hasProtoText) {
        var flaws = new List<string>();
        if (string.IsNullOrWhiteSpace(clientVersion)) flaws.Add(FlawNoClientVersion);
        if (HasPlatformBuildMismatch(platform, build)) flaws.Add(FlawBuildPlatformMismatch);
        if (!hasProtoText || string.IsNullOrWhiteSpace(protoSha)) flaws.Add(FlawNoProto);
        return flaws;
    }

    public static long LatestSortKey(string? platform, string? build, string? appVersion) {
        if (string.IsNullOrWhiteSpace(platform)) return long.MinValue;
        return platform.Equals("android", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out long vc) ? vc : long.MinValue
            : HasPlatformBuildMismatch(platform, build)
                ? long.MinValue
                : DottedVersionKey(appVersion);
    }

    public static long DottedVersionKey(string? version) {
        if (string.IsNullOrWhiteSpace(version)) return long.MinValue;
        string[] parts = version.Split('.');
        long key = 0;
        for (int i = 0; i < 4; i++) {
            long comp = 0;
            if (i < parts.Length &&
                long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) &&
                p >= 0) {
                comp = Math.Min(p, 0xFFFF);
            }

            key = (key << 16) | comp;
        }

        return key;
    }
}
