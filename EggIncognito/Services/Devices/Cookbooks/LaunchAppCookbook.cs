using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchAppCookbook(LaunchAppStep step) : IStepCookbook, IDeviceAppLauncher {
    public string Id => DeviceCookbookIds.LaunchApp;
    public string Title => "Launch app";
    public string Summary => "Resolves the launch activity and starts Egg Inc so it emits traffic.";

    public async Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        var a = await step.DescribeAsync(target, ct);
        return new DeviceCookbookInfo(Id, Title, Summary, a.Available, a.Unavailable) {
            Group = CookbookGroups.Step
        };
    }

    public Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CookbookStep>>([step]);

    public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
        CookbookExecutor.RunStepsAsync(this, context, ct);

    public async Task<DeviceCookbookRun> LaunchAsync(DeviceTarget target, Action<string> progress, CancellationToken ct) {
        var result = await step.RunAsync(new DeviceCookbookContext(target, null, progress), ct);
        return new DeviceCookbookRun(
            result.Status != CookbookStepStatus.Failed, Id, result.Lines,
            result.Status == CookbookStepStatus.Failed ? result.StepId : null, result.Note) {
            Steps = [result]
        };
    }
}
