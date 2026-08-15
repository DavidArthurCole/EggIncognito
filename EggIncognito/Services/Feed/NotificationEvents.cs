using System.Globalization;
using EggIncognito.Data.Models;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public interface INotificationEvent {
    string EventKind { get; }
    string DedupKey { get; }
    string Summary { get; }
    bool Matches(FeedSubscription sub);
    IReadOnlyList<string> BlockedBy(FeedSubscription sub);
    string BuildBody(string? messageTemplate);
}

public sealed record ProtoBuildEvent(
    int ProtoVersionId,
    string Platform,
    string AppVersion,
    string Build,
    string? ClientVersion,
    string ProtoSha,
    bool Created,
    bool ProtoChanged,
    string PageUrl,
    VersionDelta Delta = VersionDelta.Unknown,
    string? PrevAppVersion = null,
    string? PrevBuild = null,
    IReadOnlyList<string>? Flaws = null) : INotificationEvent {
    public string EventKind => FeedEventKinds.ProtoBuild;
    public string DedupKey => ProtoVersionId.ToString(CultureInfo.InvariantCulture);

    public IReadOnlyList<string> FlawList => Flaws ?? [];

    public string Summary =>
        $"{Platform} {AppVersion} ({Build}) {VersionDeltaCalc.Label(Delta)}";

    public bool Matches(FeedSubscription sub) =>
        FeedTrigger.Matches(sub.Trigger, Created, ProtoChanged, Delta, FlawList.Count > 0,
            sub.Platforms, Platform);

    public IReadOnlyList<string> BlockedBy(FeedSubscription sub) {
        if (sub.Trigger == FeedEventKinds.TriggerSuspect) return [];

        var blocked = new List<string>();
        foreach (string filter in sub.Filters) {
            bool fails = filter switch {
                FeedEventKinds.FilterRequireClientVersion =>
                    FlawList.Contains(ProtoVersionQuality.FlawNoClientVersion),
                FeedEventKinds.FilterRequireProto =>
                    FlawList.Contains(ProtoVersionQuality.FlawNoProto),
                FeedEventKinds.FilterSaneBuild =>
                    FlawList.Contains(ProtoVersionQuality.FlawBuildPlatformMismatch),
                FeedEventKinds.FilterKnownDelta => Delta == VersionDelta.Unknown,
                _ => false
            };
            if (fails) blocked.Add(filter);
        }

        return blocked;
    }

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.Build(Platform, AppVersion, Build, ClientVersion, ProtoSha, ProtoChanged, PageUrl,
            messageTemplate, Delta, PrevAppVersion, PrevBuild, FlawList);
}

public sealed record PeriodicalsAspectSummary(
    IReadOnlyList<string> ChangedAspects,
    IReadOnlyList<string> AddedEvents,
    IReadOnlyList<string> AddedContracts,
    IReadOnlyList<string> AddedColleggtibles);

public sealed record PeriodicalsChangedEvent(
    string Feed,
    string Sha,
    string PageUrl,
    PeriodicalsAspectSummary? Aspects = null) : INotificationEvent {
    public string EventKind => FeedEventKinds.PeriodicalsChanged;
    public string DedupKey => $"{Feed}:{Sha}";

    public string Summary => $"{Feed} {(Sha.Length > 12 ? Sha[..12] : Sha)}";

    public bool Matches(FeedSubscription sub) =>
        sub.Trigger is "any" || string.Equals(sub.Trigger, Feed, StringComparison.Ordinal);

    public IReadOnlyList<string> BlockedBy(FeedSubscription sub) =>
        sub.Filters.Contains(FeedEventKinds.FilterRequireAspects, StringComparer.Ordinal) && !HasIdentifiedChange
            ? [FeedEventKinds.FilterRequireAspects]
            : [];

    private bool HasIdentifiedChange =>
        Aspects is not null && (Aspects.ChangedAspects.Count > 0 || Aspects.AddedEvents.Count > 0 ||
                                Aspects.AddedContracts.Count > 0 || Aspects.AddedColleggtibles.Count > 0);

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.BuildPeriodicals(Feed, Sha, PageUrl, messageTemplate, Aspects);
}
