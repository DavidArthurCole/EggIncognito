using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Drives the on-device Google Play Store to update Egg Inc, then re-reads the installed version to see
// whether it climbed. The PHONE's Play account does the download/install (the device asks its own store);
// we only drive Play's UI over adb and then poll the version. No server-side APK push, no version list.
//
// A bare `am start market://...` page-open does NOT install on a device that is not set to auto-update: it
// just surfaces the page. The PROVEN headless drive (2026-06-18, A15) is a UI sequence: wake the screen,
// dismiss the (non-secure) keyguard, open the package's Play page, then locate the "Update" button in the
// uiautomator hierarchy and tap its center. Play then downloads + installs. Requires the device lock be
// None/Swipe (a secure PIN keyguard cannot be dismissed headlessly). We poll dumpsys until the version
// climbs or the window elapses. Never throws.
public sealed class AndroidPlayStoreChecker(
    IProcessRunner runner, AndroidPlayStoreChecker.Options opts, ILogger<AndroidPlayStoreChecker> logger)
    : IDeviceStoreChecker
{
    // DeepLinkTemplate: the `am start` line that opens the Play page, {package} substituted. PollSeconds/
    // PollAttempts bound the wait for the version to climb after the Update tap.
    public sealed record Options(string DriveTemplate, int PollSeconds, int PollAttempts);

    public string Platform => "android";

    public async Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceStoreTarget device, CancellationToken ct, Action<string>? progress = null)
    {
        progress?.Invoke("reading installed version over adb…");
        var before = await ReadInstalledAsync(device, ct);
        if (before is null)
        {
            logger.LogInformation("device check-update: {Id} android unreachable (no version read)", device.Id);
            return new StoreCheckResult(false, null, null, false, false, "unreachable", "device unreachable or no version read");
        }

        // Drive Play's UI to install the pending update. The phone's store does the download/install.
        progress?.Invoke($"installed {before}; waking device + opening Play…");
        logger.LogInformation("device check-update: {Id} android driving Play UI for {Pkg}", device.Id, device.Package);
        var drive = await DrivePlayUpdateAsync(device, progress, ct);
        if (!drive.Ok)
        {
            // Could not find/tap an Update button. Most likely already current (no Update button on the page)
            // or the page did not render. Distinguish below via the version poll; surface the reason as a note.
            if (drive.NoUpdateButton)
            {
                logger.LogInformation("device check-update: {Id} android no Update button (likely current)", device.Id);
                return new StoreCheckResult(true, before, before, false, false, "up_to_date",
                    "no Update button on the Play page (already current, or update not yet offered)");
            }
            var note = $"Play UI drive failed: {drive.Note}";
            logger.LogWarning("device check-update: {Id} android error: {Note}", device.Id, note);
            return new StoreCheckResult(true, before, before, false, false, "error", note);
        }

        // Poll the installed version: Play's download/install is async, so wait for it to climb. The progress
        // line reports elapsed time + what we are waiting on, not the unchanged version (already on the row).
        progress?.Invoke($"Update tapped; waiting for Play to install (up to {opts.PollAttempts * opts.PollSeconds}s)…");
        for (var attempt = 0; attempt < opts.PollAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(opts.PollSeconds), ct); }
            catch (OperationCanceledException) { break; }

            var now = await ReadInstalledAsync(device, ct);
            var n = attempt + 1;
            var elapsed = n * opts.PollSeconds;
            logger.LogInformation("device check-update: {Id} android poll {N}/{Max} installed={Ver}",
                device.Id, n, opts.PollAttempts, now ?? "?");
            if (now is not null && DeviceProbeRunner.SemverCompare(now, before) > 0)
            {
                progress?.Invoke($"Play installed {now} (was {before})");
                logger.LogInformation("device check-update: {Id} android climb {Before} -> {After}", device.Id, before, now);
                return new StoreCheckResult(true, before, now, true, true, "updated", $"updated {before} -> {now}");
            }
            progress?.Invoke($"waiting for Play install… {elapsed}s elapsed (no change yet)");
        }

        // No climb within the window. Either already current, or the update is still downloading. We cannot
        // distinguish "no update" from "still in flight" without a Play status read (none reliable over adb),
        // so report up_to_date with a note; a later check re-confirms.
        var last = await ReadInstalledAsync(device, ct);
        logger.LogInformation("device check-update: {Id} android up_to_date installed={Ver} (no climb in {Max}x{Sec}s)",
            device.Id, last ?? "?", opts.PollAttempts, opts.PollSeconds);
        return new StoreCheckResult(true, before, last, false, false, "up_to_date",
            $"no update applied in {opts.PollAttempts * opts.PollSeconds}s (already current, or download still in flight)");
    }

    private readonly record struct DriveOutcome(bool Ok, bool NoUpdateButton, string? Note);

    // The proven A15 sequence: wake -> dismiss keyguard -> open the Play page (deep-link template) -> dump the
    // UI hierarchy -> find the "Update" button bounds -> tap its center. Returns NoUpdateButton when the page
    // has no Update button (already current / not offered), distinct from a hard adb failure.
    private async Task<DriveOutcome> DrivePlayUpdateAsync(
        DeviceStoreTarget device, Action<string>? progress, CancellationToken ct)
    {
        // Wake + dismiss the (non-secure) keyguard so the Play page can come to the foreground.
        await Shell(device, "input keyevent KEYCODE_WAKEUP", ct);
        await Shell(device, "wm dismiss-keyguard", ct);

        var deepLink = opts.DriveTemplate.Replace("{package}", device.Package);
        var open = await Shell(device, deepLink, ct);
        if (open.ExitCode != 0)
            return new DriveOutcome(false, false, $"open page: {DeviceParsing.TrimNote(open.Stderr + open.Stdout)}");

        // Let the page render, then read the UI tree. Retry the dump a few times: the page can be mid-load.
        progress?.Invoke("Play page open; locating Update button…");
        for (var tries = 0; tries < 4; tries++)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(tries == 0 ? 6 : 3), ct); }
            catch (OperationCanceledException) { break; }

            var xml = await DumpUiAsync(device, ct);
            if (xml is null) continue;

            var bounds = FindUpdateButtonCenter(xml);
            if (bounds is { } c)
            {
                progress?.Invoke("tapping Update…");
                var tap = await Shell(device, $"input tap {c.X} {c.Y}", ct);
                if (tap.ExitCode != 0)
                    return new DriveOutcome(false, false, $"tap: {DeviceParsing.TrimNote(tap.Stderr + tap.Stdout)}");
                logger.LogInformation("device check-update: {Id} android tapped Update at {X},{Y}", device.Id, c.X, c.Y);
                return new DriveOutcome(true, false, null);
            }
            // If an Open/Uninstall button is present but no Update, the app is current -> no update offered.
            if (HasButton(xml, "Open") || HasButton(xml, "Uninstall"))
                return new DriveOutcome(false, true, "no Update button (current)");
        }
        return new DriveOutcome(false, true, "Update button not found (page may not have loaded an update)");
    }

    // uiautomator dump writes to a device file; cat it back. Returns the XML or null on failure.
    private async Task<string?> DumpUiAsync(DeviceStoreTarget device, CancellationToken ct)
    {
        var dump = await Shell(device, "uiautomator dump /sdcard/egi-ui.xml", ct);
        if (dump.ExitCode != 0) return null;
        var cat = await Shell(device, "cat /sdcard/egi-ui.xml", ct);
        return cat.ExitCode == 0 && !string.IsNullOrWhiteSpace(cat.Stdout) ? cat.Stdout : null;
    }

    // Find the "Update" node and return the center of its bounds="[l,t][r,b]". The label is a TextView whose
    // clickable parent wraps it; tapping the label center hits the parent button (proven on the A15).
    internal static (int X, int Y)? FindUpdateButtonCenter(string xml)
    {
        // Locate a node with text="Update" then the nearest bounds="..." on the same element.
        var idx = xml.IndexOf("text=\"Update\"", StringComparison.Ordinal);
        while (idx >= 0)
        {
            var b = xml.IndexOf("bounds=\"", idx, StringComparison.Ordinal);
            if (b >= 0)
            {
                var end = xml.IndexOf('"', b + 8);
                if (end > b)
                {
                    var raw = xml[(b + 8)..end]; // [l,t][r,b]
                    if (TryParseBounds(raw, out var l, out var t, out var r, out var bot))
                        return ((l + r) / 2, (t + bot) / 2);
                }
            }
            idx = xml.IndexOf("text=\"Update\"", idx + 1, StringComparison.Ordinal);
        }
        return null;
    }

    private static bool HasButton(string xml, string label) =>
        xml.Contains($"text=\"{label}\"", StringComparison.Ordinal);

    // Parse "[l,t][r,b]" into ints.
    private static bool TryParseBounds(string s, out int l, out int t, out int r, out int b)
    {
        l = t = r = b = 0;
        var nums = s.Replace("[", " ").Replace("]", " ").Replace(",", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (nums.Length != 4) return false;
        return int.TryParse(nums[0], out l) && int.TryParse(nums[1], out t)
            && int.TryParse(nums[2], out r) && int.TryParse(nums[3], out b);
    }

    private Task<ProcessResult> Shell(DeviceStoreTarget device, string cmd, CancellationToken ct) =>
        runner.RunAsync("adb", ["-s", device.Target, "shell", cmd], ct);

    private async Task<string?> ReadInstalledAsync(DeviceStoreTarget device, CancellationToken ct)
    {
        var probe = await new AdbDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }
}
