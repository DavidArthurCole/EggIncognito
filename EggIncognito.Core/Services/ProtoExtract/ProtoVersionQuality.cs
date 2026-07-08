namespace EggIncognito.Services.ProtoExtract;

// Pure data-quality rules for registry rows (proto_versions). Single source of truth so the API, the UI, and
// the "latest" sort all agree. No DB column: the flag is computed from (platform, build). The game reports the
// same internal integer build in its auxbrain BasicRequestInfo on both platforms, so a crawled/captured iOS row
// can end up with an Android-looking integer build instead of its real dotted CFBundleVersion. Those rows are
// flagged, not mutated.
public static class ProtoVersionQuality
{
    // An Android versionCode is a bare positive integer (no dots, all digits); on an iOS row it is the mismatch tell.
    public static bool IsAndroidStyleBuild(string? build) =>
        !string.IsNullOrWhiteSpace(build) && build.All(char.IsDigit);

    // True when a row's build does not match what its platform should carry: an iOS row whose build is an
    // Android-style bare integer.
    public static bool HasPlatformBuildMismatch(string? platform, string? build)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(build)) return false;
        var isIos = platform.Equals("ios", StringComparison.OrdinalIgnoreCase);
        return isIos && IsAndroidStyleBuild(build);
    }

    // Short machine-readable flag for the API/UI. null = clean.
    public static string? BuildQualityFlag(string? platform, string? build) =>
        HasPlatformBuildMismatch(platform, build) ? "android_build_on_ios" : null;

    // Platform-aware sort key for "latest": Android orders by integer versionCode, iOS by dotted CFBundleVersion
    // (a bare-integer iOS build is bad data and ranks lowest). Higher = newer; non-orderable builds rank low.
    public static long LatestSortKey(string? platform, string? build, string? appVersion)
    {
        if (string.IsNullOrWhiteSpace(platform)) return long.MinValue;
        if (platform.Equals("android", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(build, out var vc) ? vc : long.MinValue;

        // iOS: never let an Android-style integer build win. Order by the dotted app version instead.
        if (HasPlatformBuildMismatch(platform, build)) return long.MinValue;
        return DottedVersionKey(appVersion);
    }

    // Packs a dotted version ("1.35.7.1") into one sortable long: up to 4 components, 16 bits each.
    internal static long DottedVersionKey(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return long.MinValue;
        var parts = version.Split('.');
        long key = 0;
        for (var i = 0; i < 4; i++)
        {
            long comp = 0;
            if (i < parts.Length && long.TryParse(parts[i], out var p) && p >= 0) comp = Math.Min(p, 0xFFFF);
            key = (key << 16) | comp;
        }
        return key;
    }
}
