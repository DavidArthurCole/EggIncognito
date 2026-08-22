namespace EggIncognito.Models.Protos;

public sealed record FeedPreviewRow(
    string Key, string Label, string Event, bool Matches, IReadOnlyList<string> BlockedBy, string? Body);
