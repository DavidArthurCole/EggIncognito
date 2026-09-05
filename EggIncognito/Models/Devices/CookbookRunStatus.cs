using EggIncognito.Core.Services.Devices;

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
    IReadOnlyList<string>? Log,
    IReadOnlyList<CookbookStepResult>? Steps = null) {
    public bool Finished =>
        !Running
        && (string.Equals(State, "succeeded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(State, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(State, "cancelled", StringComparison.OrdinalIgnoreCase));

    public bool Succeeded => string.Equals(State, "succeeded", StringComparison.OrdinalIgnoreCase);

    public bool Cancelled => string.Equals(State, "cancelled", StringComparison.OrdinalIgnoreCase);

    public string? Note => Failure?.Note ?? Message;

    public string? FailedStep => Failure?.StepId;

    public string? FailedStepTitle => Failure?.Title;

    private CookbookStepResult? Failure => Succeeded || Cancelled
        ? null
        : Steps?.LastOrDefault(s => s.Status == CookbookStepStatus.Failed);
}
