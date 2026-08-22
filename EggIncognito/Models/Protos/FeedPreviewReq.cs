namespace EggIncognito.Models.Protos;

public sealed record FeedPreviewReq(
    string? EventKind, string? Trigger, string[]? Platforms, string[]? Filters, string? MessageTemplate);
