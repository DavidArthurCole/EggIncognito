namespace EggIncognito.Models.Devices;

public sealed record ImageBuildStatusView(
    long Id,
    string Spec,
    string Tag,
    string State,
    string? Note,
    string Log,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
