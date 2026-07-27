namespace EggIncognito.Services.Feed;

public sealed record FeedTriggerOption(string Value, string Label);

public sealed record FeedEventKindInfo(
    string Key,
    string Label,
    IReadOnlyList<FeedTriggerOption> Triggers,
    IReadOnlyList<string> Vars,
    string DefaultTrigger,
    bool PlatformScoped);

public static class FeedEventKinds {
    public const string ProtoBuild = "proto_build";
    public const string PeriodicalsChanged = "periodicals_changed";

    public static readonly FeedEventKindInfo Proto = new(
        ProtoBuild, "Proto build",
        [
            new FeedTriggerOption("proto_changed", "Proto changed"),
            new FeedTriggerOption("new_version", "Any new version")
        ],
        ["platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl"],
        "proto_changed", true);

    public static readonly FeedEventKindInfo Periodicals = new(
        PeriodicalsChanged, "Periodicals changed",
        [
            new FeedTriggerOption("any", "Any feed"),
            new FeedTriggerOption("periodicals", "get_periodicals"),
            new FeedTriggerOption("afx-config", "ei_afx/config"),
            new FeedTriggerOption("season-infos", "get_season_infos_v2")
        ],
        ["feed", "sha", "pageUrl", "changedAspects", "addedEvents", "addedContracts", "addedColleggtibles"],
        "any", false);

    public static readonly IReadOnlyList<FeedEventKindInfo> All = [Proto, Periodicals];

    public static FeedEventKindInfo? Find(string? key) => All.FirstOrDefault(k => k.Key == key);

    public static bool IsValid(string? key) => Find(key) is not null;

    public static string Normalize(string? key) => IsValid(key) ? key! : ProtoBuild;

    public static string NormalizeTrigger(string kind, string? trigger) {
        var info = Find(kind) ?? Proto;
        return info.Triggers.Any(t => t.Value == trigger) ? trigger! : info.DefaultTrigger;
    }
}
