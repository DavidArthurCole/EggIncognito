namespace EggIncognito.Models.Protos;

public sealed record FeedActivityRow(
    DateTimeOffset At, string Status, string Event, int? ResponseCode, string? Reason);
