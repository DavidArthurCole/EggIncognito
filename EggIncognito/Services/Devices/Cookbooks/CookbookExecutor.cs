using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class CookbookExecutor {
    public Task<DeviceCookbookRun> RunAsync(
        IDeviceCookbook cookbook, DeviceCookbookContext context, CancellationToken ct) =>
        cookbook is IStepCookbook step
            ? RunStepsAsync(step, context, ct)
            : RunWholeAsync(cookbook, context, ct);

    public static async Task<DeviceCookbookRun> RunStepsAsync(
        IStepCookbook cookbook, DeviceCookbookContext context, CancellationToken ct) {
        var plan = await cookbook.PlanAsync(context.Target, context.Argument, ct);
        var steps = new List<CookbookStepResult>();
        var log = new List<string>();
        string? failedStep = null;
        bool ok = true;

        foreach (var step in plan) {
            string marker = $"> {step.Title}";
            log.Add(marker);
            context.Progress(marker);

            var result = await step.RunAsync(context, ct);
            steps.Add(result);
            log.AddRange(result.Lines);

            if (result.Status == CookbookStepStatus.Failed && !step.Soft) {
                ok = false;
                failedStep = result.StepId;
                break;
            }
        }

        string? note = ok
            ? OkNote(cookbook.Id, steps)
            : FailNote(steps, failedStep);
        return new DeviceCookbookRun(ok, cookbook.Id, log, failedStep, note) { Steps = steps };
    }

    private static async Task<DeviceCookbookRun> RunWholeAsync(
        IDeviceCookbook cookbook, DeviceCookbookContext context, CancellationToken ct) {
        var run = await cookbook.RunAsync(context, ct);
        var status = run.Ok ? CookbookStepStatus.Ok : CookbookStepStatus.Failed;
        var result = new CookbookStepResult(cookbook.Id, cookbook.Title, status, run.Note, run.Log);
        return run with { Steps = [result] };
    }

    private static string OkNote(string cookbookId, List<CookbookStepResult> steps) {
        var skipped = steps.Where(s => s.Status == CookbookStepStatus.Skipped).Select(s => s.StepId).ToList();
        if (skipped.Count > 0) return $"ok, skipped {string.Join(", ", skipped)}";
        return steps.Count == 1 && steps[0].Note is { Length: > 0 } single ? single : $"{cookbookId} ok";
    }

    private static string FailNote(IReadOnlyList<CookbookStepResult> steps, string? failedStep) {
        var failed = steps.FirstOrDefault(s => s.StepId == failedStep && s.Status == CookbookStepStatus.Failed);
        return $"{failedStep ?? "?"} failed: {failed?.Note ?? "no detail"}";
    }
}
