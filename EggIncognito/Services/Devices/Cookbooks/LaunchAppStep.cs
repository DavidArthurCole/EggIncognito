using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class LaunchAppStep(IDeviceConnectionFactory connections) : CookbookStep {
    private static readonly TimeSpan ForegroundWait = TimeSpan.FromSeconds(25);

    public override string Id => DeviceCookbookIds.LaunchApp;
    public override string Title => "Launch app";

    public override async Task<CookbookStepAvailability> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return CookbookStepAvailability.No("launching by resolved activity is android-only");
        if (connections.For(target) is not { } conn)
            return CookbookStepAvailability.No("no connection for this device");

        var pm = await conn.ShellAsync($"pm path {target.Package}", ct);
        if (pm.ExitCode != 0 || !pm.Stdout.Contains("package:", StringComparison.Ordinal))
            return CookbookStepAvailability.No($"{target.Package} is not installed on this device");

        return CookbookStepAvailability.Ready;
    }

    public override async Task<CookbookStepResult> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var lines = new List<string>();
        void Add(string line) {
            lines.Add(line);
            context.Progress(line);
        }

        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Skipped(lines, "launching by resolved activity is android-only");
        if (connections.For(target) is not { } conn)
            return Failed(lines, "no connection for this device");

        Add($"resolving the launch activity for {target.Package}");
        var resolve = await conn.ShellAsync($"cmd package resolve-activity --brief {target.Package} | tail -1", ct);
        string component = resolve.Stdout.Trim();
        if (resolve.ExitCode != 0 || !component.Contains('/', StringComparison.Ordinal)) {
            return Failed(lines,
                $"no launch activity for {target.Package}: {DeviceParsing.TrimNote(resolve.Stdout + resolve.Stderr)}");
        }

        Add($"starting {component}");
        var start = await conn.ShellAsync($"am start -n {component}", ct);
        if (start.ExitCode != 0 || start.Stdout.Contains("Error", StringComparison.Ordinal)) {
            return Failed(lines,
                $"am start failed: {DeviceParsing.TrimNote(start.Stdout + start.Stderr)}");
        }

        var front = await DeviceForeground.WaitAsync(
            conn, target.Package, DeviceForeground.PlayStorePackage, ForegroundWait, ct);
        if (front.Is(DeviceForeground.PlayStorePackage))
            return Failed(lines, $"{component} started but {DeviceForeground.PlayBlockNote}");

        Add($"foreground: {front.Component ?? DeviceParsing.TrimNote(front.Raw)}");
        if (front.Is(target.Package)) return Ok(lines, $"launched {component}");

        var alive = await conn.ShellAsync($"pidof {target.Package}", ct);
        return alive.Stdout.Trim().Length > 0
            ? Failed(lines, $"{component} is running but never took the foreground in {ForegroundWait.TotalSeconds:F0}s; front is {front.Package ?? "unknown"}")
            : Failed(lines, $"{component} exited within {ForegroundWait.TotalSeconds:F0}s of starting; front is {front.Package ?? "unknown"}");
    }
}
