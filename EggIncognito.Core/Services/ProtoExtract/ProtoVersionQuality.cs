namespace EggIncognito.Services.ProtoExtract;

// Pure data-quality rules for registry rows (proto_versions). Single source of truth so the API, the UI,
// and the "latest" sort all agree. No DB column: the flag is COMPUTED from (platform, build).
//
// WHY: the game reports the SAME internal integer build (an Android-style versionCode, e.g. "111342") in its
// auxbrain BasicRequestInfo on BOTH platforms. So a crawled/captured iOS row legitimately ends up with an
// Android-looking integer build, when an iOS row's build key should be the dotted CFBundleVersion
// (e.g. "1.35.7.1") or a binary hash. Those integer-build iOS rows are mislabeled data, and they also break
// the "latest" sort (a bare-integer build sorts above real hash/dotted iOS builds). We flag them, don't
// mutate them.
public static class ProtoVersionQuality
{
    // An Android versionCode is a bare positive integer (no dots, all digits). This is the canonical Android
    // build form; on an iOS row it is the mismatch tell.
    public static bool IsAndroidStyleBuild(string? build) =>
        !string.IsNullOrWhiteSpace(build) && build.All(char.IsDigit);

    // True when a row's build does not match what its platform should carry. Currently the only known case:
    // an iOS row whose build is an Android-style bare integer (the shared wire versionCode leaking into the
    // iOS build key). Android rows with integer builds are correct; iOS rows with dotted/hash builds are fine.
    public static bool HasPlatformBuildMismatch(string? platform, string? build)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(build)) return false;
        var isIos = platform.Equals("ios", StringComparison.OrdinalIgnoreCase);
        return isIos && IsAndroidStyleBuild(build);
    }

    // Short machine-readable flag for the API/UI. null = clean.
    public static string? BuildQualityFlag(string? platform, string? build) =>
        HasPlatformBuildMismatch(platform, build) ? "android_build_on_ios" : null;

    // Platform-aware sort key for "latest": rank by the build that actually orders this platform.
    //   - Android: the integer versionCode orders releases (higher = newer).
    //   - iOS: the dotted CFBundleVersion orders releases; a bare-integer iOS build is BAD data and must NOT
    //     win "latest", so it ranks lowest.
    // Returns a comparable (long) where higher = newer within a platform. Non-orderable builds rank low.
    public static long LatestSortKey(string? platform, string? build, string? appVersion)
    {
        if (string.IsNullOrWhiteSpace(platform)) return long.MinValue;
        if (platform.Equals("android", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(build, out var vc) ? vc : long.MinValue;

        // iOS: never let an Android-style integer build win. Order by the dotted app version instead.
        if (HasPlatformBuildMismatch(platform, build)) return long.MinValue;
        return DottedVersionKey(appVersion);
    }

    // Pack a dotted version ("1.35.7.1") into one sortable long: up to 4 components, 16 bits each.
    // Higher dotted version => higher key. Missing/garbage => long.MinValue (sorts last).
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
