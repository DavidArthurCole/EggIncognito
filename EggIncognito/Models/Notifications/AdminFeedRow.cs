namespace EggIncognito.Models.Notifications;

public sealed record AdminFeedRow(
    int Id,
    Guid? OwnerUserId,
    string OwnerUsername,
    string EventKind,
    string Kind,
    string? Label,
    string TargetMasked,
    string[] Platforms,
    string Trigger,
    string[] Filters,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastDeliveryAt,
    int FailCount);
