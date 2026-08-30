namespace EggIncognito.Models.Devices;

public sealed record CookbookRunStatus(
    long JobId,
    string? DeviceId,
    string? State,
    string? Outcome,
    string? Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    bool Running,
    IReadOnlyList<string>? Log) {
    public bool Finished =>
        !Running
        && (string.Equals(State, "succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(State, "failed", StringComparison.OrdinalIgnoreCase));

    public bool Succeeded => string.Equals(State, "succeeded", StringComparison.OrdinalIgnoreCase);

    public string? Note => Message;

    public string? FailedStep => Succeeded ? null : Outcome;
}
