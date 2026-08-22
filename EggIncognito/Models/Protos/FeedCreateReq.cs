namespace EggIncognito.Models.Protos;

public sealed record FeedCreateReq(
    string WebhookUrl,
    string[]? Platforms,
    string? Trigger,
    string? Label,
    string? MessageTemplate,
    string? EventKind = null,
    string[]? Filters = null);
