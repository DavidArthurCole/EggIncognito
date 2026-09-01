using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class BringUpCookbook(
    InstallAppStep installApp,
    InstallCaStep installCa,
    LaunchAppStep launchApp,
    DismissFirstRunStep dismissFirstRun,
    RecertStep recert) : IStepCookbook {
    public string Id => DeviceCookbookIds.BringUp;
    public string Title => "Bring up";

    public string Summary =>
        "Installs the app, trusts the capture CA, launches Egg Inc, clears the first-run dialogs and recerts when rooted.";

    public async Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        var install = await installApp.DescribeAsync(target, ct);
        if (install.Available) return Info(true, null, install);

        var launch = await launchApp.DescribeAsync(target, ct);
        if (launch.Available) return Info(true, null, install);

        return Info(false, install.Unavailable ?? "nothing to run", install);
    }

    public Task<IReadOnlyList<CookbookStep>> PlanAsync(DeviceTarget target, string? argument, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CookbookStep>>(
            [installApp, installCa, launchApp, new SoftStep(dismissFirstRun), new SoftStep(recert)]);

    public Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) =>
        CookbookExecutor.RunStepsAsync(this, context, ct);

    private DeviceCookbookInfo Info(bool available, string? unavailable, CookbookStepAvailability install) =>
        new(Id, Title, Summary, available, unavailable, install.ArgumentLabel, install.Options) {
            Group = CookbookGroups.Workflow
        };
}
