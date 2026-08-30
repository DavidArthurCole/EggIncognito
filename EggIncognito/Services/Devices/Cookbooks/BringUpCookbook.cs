using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class BringUpCookbook(
    InstallAppCookbook installApp,
    InstallCaCookbook installCa,
    LaunchAppCookbook launchApp,
    DismissFirstRunCookbook dismissFirstRun) : IDeviceCookbook {
    public string Id => DeviceCookbookIds.BringUp;
    public string Title => "Bring up";

    public string Summary =>
        "Installs the app, trusts the capture CA, launches Egg Inc and clears the first-run dialogs.";

    private IReadOnlyList<IDeviceCookbook> Steps => [installApp, installCa, launchApp, dismissFirstRun];

    private static bool Soft(IDeviceCookbook cookbook) =>
        string.Equals(cookbook.Id, DeviceCookbookIds.DismissFirstRun, StringComparison.Ordinal);

    public async Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        var described = new List<DeviceCookbookInfo>();
        foreach (var step in Steps) described.Add(await step.DescribeAsync(target, ct));

        var install = described.First(d => d.Id == DeviceCookbookIds.InstallApp);
        if (!install.Available && described.All(d => !d.Available))
            return new DeviceCookbookInfo(Id, Title, Summary, false, install.Unavailable ?? "nothing to run");

        return new DeviceCookbookInfo(Id, Title, Summary, true, null, install.ArgumentLabel, install.Options);
    }

    public async Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var log = new CookbookRunLog(context.Progress);

        foreach (var step in Steps) {
            var info = await step.DescribeAsync(context.Target, ct);
            if (!info.Available) {
                log.Add($"skipping {step.Id}: {info.Unavailable ?? "unavailable"}");
                continue;
            }

            log.Add($"running {step.Id}");
            string? argument = step.Id == DeviceCookbookIds.InstallApp ? context.Argument : null;
            var run = await step.RunAsync(new DeviceCookbookContext(context.Target, argument, log.Add), ct);
            if (run.Ok || Soft(step)) continue;

            return new DeviceCookbookRun(false, Id, log.Lines, run.FailedStep ?? step.Id,
                $"{step.Id} failed: {run.Note ?? "no detail"}");
        }

        return log.Ok(Id, "bring-up complete");
    }
}
