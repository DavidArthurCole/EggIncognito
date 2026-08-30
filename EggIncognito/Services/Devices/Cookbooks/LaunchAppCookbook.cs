using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchAppCookbook(IDeviceConnectionFactory connections) : IDeviceCookbook, IDeviceAppLauncher {
    public string Id => DeviceCookbookIds.LaunchApp;
    public string Title => "Launch app";
    public string Summary => "Resolves the launch activity and starts Egg Inc so it emits traffic.";

    public async Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Unavailable("launching by resolved activity is android-only");
        if (connections.For(target) is not { } conn) return Unavailable("no connection for this device");

        var pm = await conn.ShellAsync($"pm path {target.Package}", ct);
        if (pm.ExitCode != 0 || !pm.Stdout.Contains("package:", StringComparison.Ordinal))
            return Unavailable($"{target.Package} is not installed on this device");

        return new DeviceCookbookInfo(Id, Title, Summary, true);
    }

    public async Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var log = new CookbookRunLog(context.Progress);
        var target = context.Target;

        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return log.Fail(Id, "platform", "launching by resolved activity is android-only");
        if (connections.For(target) is not { } conn)
            return log.Fail(Id, "connection", "no connection for this device");

        log.Add($"resolving the launch activity for {target.Package}");
        var resolve = await conn.ShellAsync($"cmd package resolve-activity --brief {target.Package} | tail -1", ct);
        string component = resolve.Stdout.Trim();
        if (resolve.ExitCode != 0 || !component.Contains('/', StringComparison.Ordinal)) {
            return log.Fail(Id, "resolve-activity",
                $"no launch activity for {target.Package}: {DeviceParsing.TrimNote(resolve.Stdout + resolve.Stderr)}");
        }

        log.Add($"starting {component}");
        var start = await conn.ShellAsync($"am start -n {component}", ct);
        if (start.ExitCode != 0 || start.Stdout.Contains("Error", StringComparison.Ordinal)) {
            return log.Fail(Id, "am-start",
                $"am start failed: {DeviceParsing.TrimNote(start.Stdout + start.Stderr)}");
        }

        return log.Ok(Id, $"launched {component}");
    }

    public Task<DeviceCookbookRun> LaunchAsync(DeviceTarget target, Action<string> progress, CancellationToken ct) =>
        RunAsync(new DeviceCookbookContext(target, null, progress), ct);

    private DeviceCookbookInfo Unavailable(string reason) => new(Id, Title, Summary, false, reason);
}
