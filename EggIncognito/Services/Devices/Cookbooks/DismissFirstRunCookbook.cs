using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class DismissFirstRunCookbook(DismissFirstRunStep step) : IStepCookbook {
    public string Id => DeviceCookbookIds.DismissFirstRun;
    public string Title => "Dismiss first-run dialogs";

    public string Summary =>
        "Clears the Google Play, first-run and GMS setup-wizard dialogs Egg Inc shows on a fresh install. " +
        "A no-op when none are up.";

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
}
