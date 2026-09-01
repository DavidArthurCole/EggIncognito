namespace EggIncognito.Core.Services.Devices;

public enum CookbookStepStatus {
    Ok,
    Skipped,
    Failed
}

public sealed record CookbookStepResult(
    string StepId,
    string Title,
    CookbookStepStatus Status,
    string? Note,
    IReadOnlyList<string> Lines);

public sealed record CookbookStepAvailability(
    bool Available,
    string? Unavailable = null,
    string? ArgumentLabel = null,
    IReadOnlyList<DeviceCookbookOption>? Options = null) {
    public static readonly CookbookStepAvailability Ready = new(true);

    public static CookbookStepAvailability No(string reason) => new(false, reason);
}

public abstract class CookbookStep {
    public abstract string Id { get; }
    public abstract string Title { get; }
    public virtual bool Soft => false;

    public virtual Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(CookbookStepAvailability.Ready);

    public abstract Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct);

    protected CookbookStepResult Ok(IReadOnlyList<string> lines, string? note = null) =>
        new(Id, Title, CookbookStepStatus.Ok, note, lines);

    protected CookbookStepResult Skipped(IReadOnlyList<string> lines, string? note) =>
        new(Id, Title, CookbookStepStatus.Skipped, note, lines);

    protected CookbookStepResult Failed(IReadOnlyList<string> lines, string? note) =>
        new(Id, Title, CookbookStepStatus.Failed, note, lines);
}

public sealed class SoftStep(CookbookStep inner) : CookbookStep {
    public override string Id => inner.Id;
    public override string Title => inner.Title;
    public override bool Soft => true;

    public override Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        inner.DescribeAsync(target, ct);

    public override Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
        inner.RunAsync(context, ct);
}

public static class CookbookGroups {
    public const string Workflow = "Workflow";
    public const string Step = "Step";
}

public interface IStepCookbook : IDeviceCookbook {
    Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument, CancellationToken ct);
}
