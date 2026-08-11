using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class AndroidStoreUpdateDriver(
    IProcessRunner runner,
    AndroidStoreUpdateDriver.Options opts,
    AndroidStoreCatalog catalog,
    KnownVersionRecorder knownVersions,
    ILogger<AndroidStoreUpdateDriver> logger) : IStoreUpdateDriver {
    public string Platform => Platforms.Android;
    public string StoreName => "Play";

    public async Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct) {
        var probe = await new AdbDeviceProbe(runner, target.Target, target.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }

    public async Task PrepareAsync(DeviceTarget target, CancellationToken ct) {
        try {
            await Shell(target, "input keyevent KEYCODE_WAKEUP", ct);
            await Shell(target, "wm dismiss-keyguard", ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device {Id} wake best-effort failed", target.Id);
        }
    }

    public async Task<StoreProbeOutcome> ProbeStoreAsync(
        DeviceTarget target, string installed, Action<string>? progress, CancellationToken ct) {
        string? latest = await catalog.LatestVersionAsync(target.Package, opts.LookupCountry, opts.LookupLocale, ct);
        if (latest is not null) {
            await knownVersions.RecordAsync(Platforms.Android, latest, "play-scrape", ct);
            if (DeviceParsing.CompareVersions(latest, installed) <= 0) {
                return new StoreProbeOutcome(StoreAvailability.UpToDate, latest,
                    $"Play latest {latest}; installed {installed} current");
            }

            progress?.Invoke($"Play lists {latest} (installed {installed}); opening the Play page…");
        }

        return await ProbeViaUiAsync(target, latest, progress, ct);
    }

    private async Task<StoreProbeOutcome> ProbeViaUiAsync(
        DeviceTarget target, string? latest, Action<string>? progress, CancellationToken ct) {
        string deepLink = opts.DriveTemplate.Replace("{package}", target.Package);
        var open = await Shell(target, deepLink, ct);
        if (open.ExitCode != 0) {
            return new StoreProbeOutcome(StoreAvailability.Unknown, latest,
                $"open page: {DeviceParsing.TrimNote(open.Stderr + open.Stdout)}");
        }

        progress?.Invoke("Play page open; locating Update button…");
        for (int tries = 0; tries < 3; tries++) {
            int wait = tries == 0 ? opts.UiFirstWaitSeconds : opts.UiRetryWaitSeconds;
            try {
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            } catch (OperationCanceledException) {
                break;
            }

            string? xml = await DumpUiAsync(target, ct);
            if (xml is null) continue;

            if (FindUpdateButtonCenter(xml) is not null)
                return new StoreProbeOutcome(StoreAvailability.UpdateOffered, latest, null);

            if (HasButton(xml, "Open") || HasButton(xml, "Uninstall")) {
                if (AdvertisesUpdate(xml)) {
                    return new StoreProbeOutcome(StoreAvailability.ManualNeeded, latest,
                        "Play advertises an update but no auto-tappable Update button (major update?); needs manual update");
                }

                return latest is null
                    ? new StoreProbeOutcome(StoreAvailability.UpToDate, null,
                        "no Update button on the Play page (already current, or update not yet offered)")
                    : new StoreProbeOutcome(StoreAvailability.ManualNeeded, latest,
                        $"Play lists {latest} but this device has no Update button yet (staged rollout); needs manual update");
            }
        }

        return new StoreProbeOutcome(StoreAvailability.Unknown, latest,
            "Play page did not load an Update/Open/Uninstall button (store may be offline or slow)");
    }

    public async Task<TriggerOutcome> TriggerInstallAsync(
        DeviceTarget target, Action<string>? progress, CancellationToken ct) {
        string? xml = await DumpUiAsync(target, ct);
        if (xml is null) return new TriggerOutcome(false, "could not dump Play UI");

        if (FindUpdateButtonCenter(xml) is not { } c)
            return new TriggerOutcome(false, "no Update button on the Play page");

        progress?.Invoke("tapping Update…");
        var tap = await Shell(target, $"input tap {c.X} {c.Y}", ct);
        if (tap.ExitCode != 0)
            return new TriggerOutcome(false, $"tap: {DeviceParsing.TrimNote(tap.Stderr + tap.Stdout)}");
        logger.LogInformation("device check-update: {Id} android tapped Update at {X},{Y}", target.Id, c.X, c.Y);
        return new TriggerOutcome(true, null);
    }

    public async Task<bool> ProbeInstallCompleteAsync(DeviceTarget target, CancellationToken ct) {
        string? xml = await DumpUiAsync(target, ct);
        if (xml is null) return false;
        return (HasButton(xml, "Play") || HasButton(xml, "Open"))
               && HasButton(xml, "Uninstall")
               && FindUpdateButtonCenter(xml) is null;
    }

    public async Task CleanupAsync(DeviceTarget target, CancellationToken ct) {
        try {
            await Shell(target, "input keyevent KEYCODE_HOME", ct);
            await Shell(target, "input keyevent KEYCODE_SLEEP", ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device {Id} screen-sleep best-effort failed", target.Id);
        }
    }

    private async Task<string?> DumpUiAsync(DeviceTarget target, CancellationToken ct) {
        var dump = await Shell(target, "uiautomator dump /sdcard/egi-ui.xml", ct);
        if (dump.ExitCode != 0) return null;
        var cat = await Shell(target, "cat /sdcard/egi-ui.xml", ct);
        return cat.ExitCode == 0 && !string.IsNullOrWhiteSpace(cat.Stdout) ? cat.Stdout : null;
    }


    internal static (int X, int Y)? FindUpdateButtonCenter(string xml) {
        int idx = xml.IndexOf("text=\"Update\"", StringComparison.Ordinal);
        while (idx >= 0) {
            int b = xml.IndexOf("bounds=\"", idx, StringComparison.Ordinal);
            if (b >= 0) {
                int end = xml.IndexOf('"', b + 8);
                if (end > b) {
                    string raw = xml[(b + 8)..end];
                    if (TryParseBounds(raw, out int l, out int t, out int r, out int bot))
                        return ((l + r) / 2, (t + bot) / 2);
                }
            }

            idx = xml.IndexOf("text=\"Update\"", idx + 1, StringComparison.Ordinal);
        }

        return null;
    }

    private static bool HasButton(string xml, string label) =>
        xml.Contains($"text=\"{label}\"", StringComparison.Ordinal);

    private static bool AdvertisesUpdate(string xml) =>
        xml.Contains("Update available", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseBounds(string s, out int l, out int t, out int r, out int b) {
        l = t = r = b = 0;
        string[] nums = s.Replace('[', ' ').Replace(']', ' ').Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return nums.Length == 4 && int.TryParse(nums[0], out l) && int.TryParse(nums[1], out t)
               && int.TryParse(nums[2], out r) && int.TryParse(nums[3], out b);
    }

    private Task<ProcessResult> Shell(DeviceTarget target, string cmd, CancellationToken ct) =>
        runner.RunAsync("adb", ["-s", target.Target, "shell", cmd], ct);


    public sealed record Options(
        string DriveTemplate,
        int UiFirstWaitSeconds = 3,
        int UiRetryWaitSeconds = 2,
        string? LookupCountry = null,
        string? LookupLocale = null);
}
