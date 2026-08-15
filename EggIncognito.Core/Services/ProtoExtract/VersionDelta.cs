namespace EggIncognito.Services.ProtoExtract;

public enum VersionDelta {
    Unknown,
    Forward,
    Repeat,
    Backfill
}

public static class VersionDeltaCalc {
    public static VersionDelta Classify(
        string? platform, string? build, string? appVersion,
        string? prevBuild, string? prevAppVersion) {
        long key = ProtoVersionQuality.LatestSortKey(platform, build, appVersion);
        if (key == long.MinValue) return VersionDelta.Unknown;

        long prev = ProtoVersionQuality.LatestSortKey(platform, prevBuild, prevAppVersion);
        if (prev == long.MinValue) return VersionDelta.Forward;

        return key > prev ? VersionDelta.Forward : key == prev ? VersionDelta.Repeat : VersionDelta.Backfill;
    }

    public static string Label(VersionDelta delta) => delta switch {
        VersionDelta.Forward => "forward",
        VersionDelta.Repeat => "repeat",
        VersionDelta.Backfill => "backfill",
        _ => "unknown"
    };
}
