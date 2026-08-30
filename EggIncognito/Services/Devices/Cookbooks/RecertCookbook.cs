using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class RecertCookbook(
    IServiceScopeFactory scopeFactory,
    DeviceRecertConfig config) : IDeviceCookbook {
    public string Id => DeviceCookbookIds.Recert;
    public string Title => "Recert";

    public string Summary =>
        "Runs the TrickyStore/KsuWebUi keybox recert flow (Magisk fallback, Play Protect verify) on this device.";

    public Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Task.FromResult(Unavailable("recert is android-only"));
        if (!config.Enabled) return Task.FromResult(Unavailable("recert is not enabled"));
        if (string.IsNullOrEmpty(config.KsuWebUiPackage))
            return Task.FromResult(Unavailable("recert is not configured (KsuWebUiPackage missing)"));

        return Task.FromResult(new DeviceCookbookInfo(Id, Title, Summary, true));
    }

    public async Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var log = new CookbookRunLog(context.Progress);
        var target = context.Target;

        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return log.Fail(Id, "platform", "recert is android-only");

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceRecertService)) is not DeviceRecertService recert)
            return log.Fail(Id, "config", "no database configured, recert requires DeviceRecertService");

        log.Add($"recertifying {target.Id}");
        var result = await recert.RunFlowAsync(target, ct);
        log.AddRange(result.Log);

        return result.Ok
            ? log.Ok(Id, "recert ok")
            : new DeviceCookbookRun(false, Id, log.Lines, result.FailedStep, "recert failed");
    }

    private DeviceCookbookInfo Unavailable(string reason) => new(Id, Title, Summary, false, reason);
}
