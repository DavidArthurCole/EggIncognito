using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class ActivateIntegrityCookbook(ActivateIntegrityStep step) : IStepCookbook {
    public string Id => DeviceCookbookIds.ActivateIntegrity;
    public string Title => "Activate integrity chain";

    public string Summary =>
        "Runs Integrity-Box's action script: fetches the keybox, refreshes the Pixel fingerprint, writes the tricky-store targets, syncs TEESimulator's identity, then clears Play so it re-evaluates the device.";

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
