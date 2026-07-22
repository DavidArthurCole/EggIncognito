namespace EggIncognito.Services.Feed;

public sealed record FeedTriggerOption(string Value, string Label);

public sealed record FeedEventKindInfo(
    string Key, string Label, IReadOnlyList<FeedTriggerOption> Triggers,
    IReadOnlyList<string> Vars, string DefaultTrigger, bool PlatformScoped);

public static class FeedEventKinds {
    public const string ProtoBuild = "proto_build";
    public const string PeriodicalsChanged = "periodicals_changed";

    public static readonly FeedEventKindInfo Proto = new(
        ProtoBuild, "Proto build",
        [new("proto_changed", "Proto changed"), new("new_version", "Any new version")],
        ["platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl"],
        "proto_changed", PlatformScoped: true);

    public static readonly FeedEventKindInfo Periodicals = new(
        PeriodicalsChanged, "Periodicals changed",
        [
            new("any", "Any feed"),
            new("periodicals", "get_periodicals"),
            new("afx-config", "ei_afx/config"),
            new("season-infos", "get_season_infos_v2"),
        ],
        ["feed", "sha", "pageUrl"],
        "any", PlatformScoped: false);

    public static readonly IReadOnlyList<FeedEventKindInfo> All = [Proto, Periodicals];

    public static FeedEventKindInfo? Find(string? key) => All.FirstOrDefault(k => k.Key == key);

    public static bool IsValid(string? key) => Find(key) is not null;

    public static string Normalize(string? key) => IsValid(key) ? key! : ProtoBuild;

    public static string NormalizeTrigger(string kind, string? trigger) {
        var info = Find(kind) ?? Proto;
        return info.Triggers.Any(t => t.Value == trigger) ? trigger! : info.DefaultTrigger;
    }
}
