using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class DismissFirstRunCookbook(IEnumerable<IDeviceUiDriver> uiDrivers) : IDeviceCookbook {
    public string Id => DeviceCookbookIds.DismissFirstRun;
    public string Title => "Dismiss first-run dialogs";

    public string Summary =>
        "Clears the Google Play and first-run dialogs Egg Inc shows on a fresh install. A no-op when none are up.";

    public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(Driver(target.Platform) is null
            ? new DeviceCookbookInfo(Id, Title, Summary, false, $"no ui driver for platform '{target.Platform}'")
            : new DeviceCookbookInfo(Id, Title, Summary, true));

    public async Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var log = new CookbookRunLog(context.Progress);
        if (Driver(context.Target.Platform) is not { } ui)
            return log.Fail(Id, "ui-driver", $"no ui driver for platform '{context.Target.Platform}'");

        var runner = new DeviceFlowRunner(ui);
        var result = await runner.RunAsync(context.Target, FirstRunDialogFlow.Build(), log.Add, ct);
        return result.Ok
            ? log.Ok(Id, "first-run dialogs cleared or already absent")
            : new DeviceCookbookRun(false, Id, log.Lines, result.FailedStep, "first-run flow failed");
    }

    private IDeviceUiDriver? Driver(string platform) =>
        uiDrivers.FirstOrDefault(u => Platforms.Matches(u.Platform, platform));
}
