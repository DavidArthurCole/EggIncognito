using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class SeedAuditCookbook(SeedAuditStep step) : IStepCookbook {
    public string Id => DeviceCookbookIds.SeedAudit;
    public string Title => "Seed audit";
    public string Summary => "Dumps everything the first-boot seed left on a seeded image: rc, service state, state file, log, module dirs, adb keys.";

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
