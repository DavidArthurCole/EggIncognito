namespace EggIncognito.Models.Devices;

public sealed record JobGroupRow(
    long Id,
    string Kind,
    string State,
    string Trigger,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Outcome,
    string? Message,
    string? AppVersion,
    string? Build,
    string? Revision,
    int Repeat,
    DateTimeOffset LastAt);
