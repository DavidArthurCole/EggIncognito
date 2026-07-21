using EggIncognito.Data.Models;

namespace EggIncognito.Services.Feed;

public interface INotificationEvent
{
    string EventKind { get; }
    string DedupKey { get; }
    bool Matches(FeedSubscription sub);
    string BuildBody(string? messageTemplate);
}

public sealed record ProtoBuildEvent(
    int ProtoVersionId, string Platform, string AppVersion, string Build, string? ClientVersion,
    string ProtoSha, bool Created, bool ProtoChanged, string PageUrl) : INotificationEvent
{
    public string EventKind => FeedEventKinds.ProtoBuild;
    public string DedupKey => ProtoVersionId.ToString();

    public bool Matches(FeedSubscription sub) =>
        FeedTrigger.Matches(sub.Trigger, Created, ProtoChanged, sub.Platforms, Platform);

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.Build(Platform, AppVersion, Build, ClientVersion, ProtoSha, ProtoChanged, PageUrl, messageTemplate);
}

public sealed record PeriodicalsChangedEvent(string Feed, string Sha, string PageUrl) : INotificationEvent
{
    public string EventKind => FeedEventKinds.PeriodicalsChanged;
    public string DedupKey => $"{Feed}:{Sha}";

    public bool Matches(FeedSubscription sub) =>
        sub.Trigger is "any" || string.Equals(sub.Trigger, Feed, StringComparison.Ordinal);

    public string BuildBody(string? messageTemplate) =>
        DiscordFeedPayload.BuildPeriodicals(Feed, Sha, PageUrl, messageTemplate);
}
