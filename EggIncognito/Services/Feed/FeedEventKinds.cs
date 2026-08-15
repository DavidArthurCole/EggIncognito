namespace EggIncognito.Services.Feed;

public sealed record FeedTriggerOption(string Value, string Label);

public sealed record FeedFilterOption(string Key, string Label, bool DefaultOn);

public sealed record FeedEventKindInfo(
    string Key,
    string Label,
    IReadOnlyList<FeedTriggerOption> Triggers,
    IReadOnlyList<string> Vars,
    string DefaultTrigger,
    bool PlatformScoped,
    IReadOnlyList<FeedFilterOption> Filters);

public static class FeedEventKinds {
    public const string ProtoBuild = "proto_build";
    public const string PeriodicalsChanged = "periodicals_changed";

    public const string TriggerVersionUp = "version_up";
    public const string TriggerProtoChanged = "proto_changed";
    public const string TriggerNewVersion = "new_version";
    public const string TriggerSuspect = "suspect";

    public const string FilterRequireClientVersion = "require_client_version";
    public const string FilterRequireProto = "require_proto";
    public const string FilterSaneBuild = "sane_build";
    public const string FilterKnownDelta = "known_delta";
    public const string FilterRequireAspects = "require_aspects";

    public static readonly FeedEventKindInfo Proto = new(
        ProtoBuild, "Proto build",
        [
            new FeedTriggerOption(TriggerVersionUp, "New version"),
            new FeedTriggerOption(TriggerProtoChanged, "Proto changed"),
            new FeedTriggerOption(TriggerNewVersion, "Any registry insert"),
            new FeedTriggerOption(TriggerSuspect, "Suspect extraction")
        ],
        [
            "platform", "appVersion", "build", "clientVersion", "protoSha", "protoChanged", "pageUrl",
            "delta", "prevAppVersion", "prevBuild", "flaws"
        ],
        TriggerVersionUp, true,
        [
            new FeedFilterOption(FilterRequireClientVersion, "Require client version", true),
            new FeedFilterOption(FilterRequireProto, "Require proto", true),
            new FeedFilterOption(FilterSaneBuild, "Require sane build", true),
            new FeedFilterOption(FilterKnownDelta, "Require known delta", true)
        ]);

    public static readonly FeedEventKindInfo Periodicals = new(
        PeriodicalsChanged, "Periodicals changed",
        [
            new FeedTriggerOption("any", "Any feed"),
            new FeedTriggerOption("periodicals", "get_periodicals"),
            new FeedTriggerOption("afx-config", "ei_afx/config"),
            new FeedTriggerOption("season-infos", "get_season_infos_v2")
        ],
        ["feed", "sha", "pageUrl", "changedAspects", "addedEvents", "addedContracts", "addedColleggtibles"],
        "any", false,
        [new FeedFilterOption(FilterRequireAspects, "Require identified change", false)]);

    public static readonly IReadOnlyList<FeedEventKindInfo> All = [Proto, Periodicals];

    public static FeedEventKindInfo? Find(string? key) => All.FirstOrDefault(k => k.Key == key);

    public static bool IsValid(string? key) => Find(key) is not null;

    public static string Normalize(string? key) => IsValid(key) ? key! : ProtoBuild;

    public static string NormalizeTrigger(string kind, string? trigger) {
        var info = Find(kind) ?? Proto;
        return info.Triggers.Any(t => t.Value == trigger) ? trigger! : info.DefaultTrigger;
    }


    public static string[] NormalizeFilters(string kind, IEnumerable<string>? filters) {
        var info = Find(kind) ?? Proto;
        return filters is null
            ? [.. info.Filters.Where(f => f.DefaultOn).Select(f => f.Key)]
            : [.. info.Filters.Where(f => filters.Contains(f.Key, StringComparer.Ordinal)).Select(f => f.Key)];
    }
}
