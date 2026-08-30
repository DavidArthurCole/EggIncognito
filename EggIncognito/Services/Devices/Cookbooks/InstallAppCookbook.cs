using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;

namespace EggIncognito.Services.Devices.Cookbooks;

public sealed class InstallAppCookbook(
    IServiceScopeFactory scopeFactory,
    IDeviceFleet fleet,
    IProcessRunner runner) : IDeviceCookbook {
    public const string StorePrefix = "store:";
    public const string DevicePrefix = "device:";
    private static readonly TimeSpan AdbTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(5);

    public string Id => DeviceCookbookIds.InstallApp;
    public string Title => "Install app";

    public string Summary =>
        "Installs Egg Inc from the stored apk splits, or pulls a fresh copy off a physical android device first.";

    public async Task<DeviceCookbookInfo> DescribeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return Unavailable("installing the app is android-only");

        var options = new List<DeviceCookbookOption>();
        foreach (var set in await StoredSetsAsync(target.Package, ct))
            options.Add(new DeviceCookbookOption(StorePrefix + set.Key, set.Label, options.Count == 0));
        foreach (var source in await SourceDevicesAsync(target.Id, ct)) {
            options.Add(new DeviceCookbookOption(
                DevicePrefix + source.Id, $"pull fresh from {source.Label}", options.Count == 0));
        }

        if (options.Count == 0) {
            return Unavailable(
                $"no stored apk for {target.Package} and no physical android device is reachable to pull one from");
        }

        return new DeviceCookbookInfo(Id, Title, Summary, true, null, "Source", options);
    }

    public async Task<DeviceCookbookRun> RunAsync(DeviceCookbookContext context, CancellationToken ct) {
        var log = new CookbookRunLog(context.Progress);
        var target = context.Target;
        if (!Platforms.Matches(target.Platform, Platforms.Android))
            return log.Fail(Id, "platform", "installing the app is android-only");

        string? argument = context.Argument;
        if (string.IsNullOrWhiteSpace(argument)) {
            var described = await DescribeAsync(target, ct);
            var options = described.Options;
            argument = options?.FirstOrDefault(o => o.Recommended)?.Value
                       ?? (options is { Count: > 0 } ? options[0].Value : null);
            if (argument is null)
                return log.Fail(Id, "source", described.Unavailable ?? "no install source is available");
            log.Add($"no source given, using {argument}");
        }

        (var splits, string? error) = argument.StartsWith(DevicePrefix, StringComparison.Ordinal)
            ? await PullAsync(argument[DevicePrefix.Length..], log, ct)
            : await LoadAsync(Trim(argument, StorePrefix), target, log, ct);
        if (splits is null || error is not null) return log.Fail(Id, "source", error ?? "no splits resolved");

        return await InstallAsync(target, splits, log, ct);
    }

    public async Task<DeviceCookbookRun> InstallAsync(
        DeviceTarget target, IReadOnlyList<CookbookApkSplit> splits, CookbookRunLog log, CancellationToken ct) {
        var ordered = splits
            .OrderByDescending(s => string.Equals(s.Split, ApkSplitNames.Base, StringComparison.OrdinalIgnoreCase))
            .ThenBy(s => s.Split, StringComparer.Ordinal)
            .ToList();

        var staged = new List<string>();
        try {
            foreach (var split in ordered) {
                string path = DeviceShell.NewTempPath($"-{split.Split}.apk");
                await File.WriteAllBytesAsync(path, split.Bytes, ct);
                staged.Add(path);
            }

            log.Add($"installing {ordered.Count} split(s): {string.Join(", ", ordered.Select(s => s.Split))}");
            var install = await Adb(target.Target, ["install-multiple", "-r", .. staged], InstallTimeout, ct);
            if (install.ExitCode != 0) {
                return log.Fail(Id, "install-multiple",
                    $"install-multiple failed: {DeviceParsing.TrimNote(install.Stderr + install.Stdout)}");
            }
        } finally {
            foreach (string path in staged) DeviceShell.TryDelete(path);
        }

        log.Add($"{target.Package} installed");
        return log.Ok(Id, $"installed {ordered.Count} split(s)");
    }

    public async Task<(IReadOnlyList<CookbookApkSplit>? Splits, string? Error)> PullAsync(
        string sourceId, CookbookRunLog log, CancellationToken ct) {
        var source = (await fleet.EnabledAsync(ct)).FirstOrDefault(d =>
            string.Equals(d.Id, sourceId, StringComparison.Ordinal));
        if (source is null) return (null, $"unknown source device '{sourceId}'");
        if (!Platforms.Matches(source.Platform, Platforms.Android))
            return (null, $"source device '{sourceId}' is not android");
        if (DeviceOrigins.IsVirtual(source.Origin))
            return (null, $"source device '{sourceId}' is virtual; splits come off physical devices only");

        log.Add($"pulling {source.Package} splits from {source.Label}");
        var puller = new DeviceApkPuller(runner);
        byte[]? baseApk = await puller.PullBaseSplitAsync(source.Target, source.Package, ct);
        byte[]? armApk = await puller.PullArmSplitAsync(source.Target, source.Package, ct);
        if (baseApk is null) return (null, $"could not pull the base split from {source.Id}");
        if (armApk is null) return (null, $"could not pull the arm64 split from {source.Id}");

        CookbookApkSplit[] splits = [new(ApkSplitNames.Base, baseApk), new(ApkSplitNames.Arm64, armApk)];
        log.Add($"pulled base ({baseApk.LongLength} bytes) and arm64 ({armApk.LongLength} bytes)");

        (string? appVersion, string? build) = await SourceVersionAsync(source, ct);
        if (appVersion is null || build is null) {
            log.Add($"could not read the installed version off {source.Id}, splits not stored");
        } else {
            await StoreAsync(source, appVersion, build, splits, log, ct);
        }

        return (splits, null);
    }

    private async Task StoreAsync(DeviceEntry source, string appVersion, string build,
        IReadOnlyList<CookbookApkSplit> splits, CookbookRunLog log, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store) {
            log.Add("no database configured, splits not stored");
            return;
        }

        int stored = 0;
        foreach (var split in splits) {
            if (await store.PutAsync(Platforms.Android, source.Package, appVersion, build, split.Split,
                    split.Bytes, source.Id, ct)) {
                stored++;
            }
        }

        log.Add(stored == 0
            ? $"apk store already had {appVersion} ({build})"
            : $"stored {stored} split(s) as {appVersion} ({build})");
    }

    private async Task<(IReadOnlyList<CookbookApkSplit>? Splits, string? Error)> LoadAsync(
        string key, DeviceTarget target, CookbookRunLog log, CancellationToken ct) {
        int at = key.LastIndexOf('@');
        if (at < 0) return (null, $"malformed stored-apk key '{key}' (expected appVersion@build)");
        string appVersion = key[..at];
        string build = key[(at + 1)..];

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store)
            return (null, "no database configured, there is no apk store to install from");

        var rows = await store.SplitsAsync(Platforms.Android, target.Package, appVersion, build, ct);
        if (rows.Count == 0) return (null, $"the apk store has no splits for {appVersion} ({build})");
        if (!rows.Any(r => string.Equals(r.Split, ApkSplitNames.Base, StringComparison.OrdinalIgnoreCase)))
            return (null, $"the apk store has no base split for {appVersion} ({build})");

        log.Add($"loaded {rows.Count} stored split(s) for {appVersion} ({build})");
        return ([.. rows.Select(r => new CookbookApkSplit(r.Split, r.Bytes))], null);
    }

    private async Task<(string? AppVersion, string? Build)> SourceVersionAsync(
        DeviceEntry source, CancellationToken ct) {
        var dump = await Adb(source.Target, ["shell", $"dumpsys package {source.Package}"], AdbTimeout, ct);
        return dump.ExitCode != 0 ? (null, null) : DeviceParsing.AndroidVersion(dump.Stdout);
    }

    private async Task<IReadOnlyList<ApkVersionSet>> StoredSetsAsync(string package, CancellationToken ct) {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(ApkStore)) is not ApkStore store) return [];
        var sets = await store.VersionsAsync(Platforms.Android, package, ct);
        return [.. sets.Where(s => s.Installable)];
    }

    private async Task<IReadOnlyList<DeviceEntry>> SourceDevicesAsync(string excludeId, CancellationToken ct) {
        var candidates = (await fleet.EnabledAsync(ct))
            .Where(d => Platforms.Matches(d.Platform, Platforms.Android)
                        && !DeviceOrigins.IsVirtual(d.Origin)
                        && !string.Equals(d.Id, excludeId, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0) return [];

        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService(typeof(DeviceJobStore)) is not DeviceJobStore jobs) return candidates;

        var probes = await jobs.LatestPerDeviceAsync(DeviceJobKinds.Probe, ct);
        var reachable = probes.Where(p => p.Reachable == true).Select(p => p.DeviceId)
            .ToHashSet(StringComparer.Ordinal);
        return [.. candidates.Where(d => reachable.Contains(d.Id))];
    }

    private async Task<ProcessResult> Adb(string serial, string[] rest, TimeSpan timeout, CancellationToken ct) {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await runner.RunAsync("adb", ["-s", serial, .. rest], cts.Token);
    }

    private static string Trim(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;

    private DeviceCookbookInfo Unavailable(string reason) =>
        new(Id, Title, Summary, false, reason, "Source", []);
}
