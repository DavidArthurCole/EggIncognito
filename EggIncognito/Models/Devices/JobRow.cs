namespace EggIncognito.Models.Devices;

public sealed record JobRow(
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
    List<JobLineRow> Lines);
