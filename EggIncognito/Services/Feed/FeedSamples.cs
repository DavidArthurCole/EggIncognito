using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public sealed record FeedSample(string Key, string Label, INotificationEvent Event);

public static class FeedSamples {
    private const string SampleSha = "4a17bc8f0402d1e6b8c3f95a2e7d40b1c6839fae";
    private const string SampleUrl = FeedDispatcher.DefaultPageBaseUrl;

    private static ProtoBuildEvent Proto(
        string platform, string appVersion, string build, string? clientVersion, string sha,
        bool created, bool protoChanged, VersionDelta delta, string? prevAppVersion, string? prevBuild,
        bool hasProto) =>
        new(0, platform, appVersion, build, clientVersion, sha, created, protoChanged,
            FeedDispatcher.BuildPageUrl(SampleUrl, platform, build), delta, prevAppVersion, prevBuild,
            ProtoVersionQuality.Flaws(platform, appVersion, build, clientVersion, sha, hasProto));

    private static readonly IReadOnlyList<FeedSample> ProtoSamples = [
        new("forward", "New version",
            Proto("android", "1.37.0", "111358", "72", SampleSha, true, true,
                VersionDelta.Forward, "1.36.4", "111357", true)),
        new("proto_only", "Same version, proto changed",
            Proto("android", "1.37.0", "111358", "72", SampleSha, false, true,
                VersionDelta.Repeat, "1.37.0", "111358", true)),
        new("backfill", "Older build registered",
            Proto("android", "1.36.0", "111350", "71", SampleSha, true, true,
                VersionDelta.Backfill, "1.37.0", "111358", true)),
        new("broken", "Failed extraction",
            Proto("ios", "1.36.4", "111340", null, "", true, true,
                VersionDelta.Unknown, "1.37.0", "1.37.0.1", false))
    ];

    private static readonly IReadOnlyList<FeedSample> PeriodicalsSamples = [
        new("identified", "Identified change",
            new PeriodicalsChangedEvent("periodicals", SampleSha, $"{SampleUrl}/periodicals",
                new PeriodicalsAspectSummary(["events", "contracts"], ["Egg Boost"], ["hab-rush-2026"], []))),
        new("bare", "Fixture changed, nothing identified",
            new PeriodicalsChangedEvent("periodicals", SampleSha, $"{SampleUrl}/periodicals"))
    ];

    private static readonly Dictionary<string, IReadOnlyList<FeedSample>> ByKind = new(StringComparer.Ordinal) {
        [FeedEventKinds.ProtoBuild] = ProtoSamples,
        [FeedEventKinds.PeriodicalsChanged] = PeriodicalsSamples
    };

    public static IReadOnlyList<FeedSample> For(string? eventKind) =>
        eventKind is not null && ByKind.TryGetValue(eventKind, out var samples) ? samples : [];

    public static FeedSample? Find(string? eventKind, string? key) =>
        For(eventKind).FirstOrDefault(s => s.Key == key);
}
