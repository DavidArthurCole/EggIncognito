namespace EggIncognito.Models.Notifications;

public record NotificationRow(
    int Id,
    string? Label,
    string EventKind,
    string[] Platforms,
    string Trigger,
    string[] Filters,
    bool Active,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastDeliveryAt,
    int FailCount,
    string? MessageTemplate,
    string UrlMasked);
