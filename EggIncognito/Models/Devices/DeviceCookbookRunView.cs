namespace EggIncognito.Models.Devices;

public sealed record DeviceCookbookRunView(
    long JobId,
    string DeviceId,
    string State,
    string? Outcome,
    string? Message,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    bool Running,
    IReadOnlyList<string> Log);
