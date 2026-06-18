using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Drives the on-device Google Play Store to update Egg Inc, then re-reads the installed version to see
// whether it climbed. The PHONE's Play account does the download/install (the device asks its own store);
// we only nudge Play over adb and then poll the version. No server-side APK push, no version list.
//
// Play exposes no stable public "update now" CLI, and the exact mechanism varies by Play version + device,
// so the drive step is a CONFIG-TEMPLATED adb shell command (DeviceCheck:Android:DriveCommand) with a
// {package} placeholder. Default opens the store page so Play surfaces + auto-applies a pending update on a
// device set to auto-update; operators can swap in a broadcast/service invocation tuned on their device. We
// then poll dumpsys until the version climbs or the window elapses. Never throws.
public sealed class AndroidPlayStoreChecker(
    IProcessRunner runner, AndroidPlayStoreChecker.Options opts, ILogger<AndroidPlayStoreChecker> logger)
    : IDeviceStoreChecker
{
    // DriveTemplate: an adb `shell` command line (without the leading "adb -s <serial> shell"), {package}
    // substituted. PollSeconds/PollAttempts bound the wait for the version to climb after the nudge.
    public sealed record Options(string DriveTemplate, int PollSeconds, int PollAttempts);

    public string Platform => "android";

    public async Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceStoreTarget device, CancellationToken ct, Action<string>? progress = null)
    {
        var before = await ReadInstalledAsync(device, ct);
        if (before is null)
        {
            logger.LogInformation("device check-update: {Id} android unreachable (no version read)", device.Id);
            return new StoreCheckResult(false, null, null, false, false, "unreachable", "device unreachable or no version read");
        }

        // Nudge Play to check + update this package. The phone's store does the work.
        var driveArgs = BuildDriveArgs(device);
        logger.LogInformation("device check-update: {Id} android driving Play for {Pkg}", device.Id, device.Package);
        var drive = await runner.RunAsync("adb", driveArgs, ct);
        if (drive.ExitCode != 0)
        {
            var note = $"Play drive failed: {DeviceParsing.TrimNote(drive.Stderr + drive.Stdout)}";
            logger.LogWarning("device check-update: {Id} android error: {Note}", device.Id, note);
            return new StoreCheckResult(true, before, before, false, false, "error", note);
        }

        // Poll the installed version: Play's download/install is async, so wait for it to climb.
        for (var attempt = 0; attempt < opts.PollAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(opts.PollSeconds), ct); }
            catch (OperationCanceledException) { break; }

            var now = await ReadInstalledAsync(device, ct);
            var n = attempt + 1;
            progress?.Invoke($"poll {n}/{opts.PollAttempts}: installed {now ?? "?"}");
            logger.LogInformation("device check-update: {Id} android poll {N}/{Max} installed={Ver}",
                device.Id, n, opts.PollAttempts, now ?? "?");
            if (now is not null && DeviceProbeRunner.SemverCompare(now, before) > 0)
            {
                logger.LogInformation("device check-update: {Id} android climb {Before} -> {After}", device.Id, before, now);
                return new StoreCheckResult(true, before, now, true, true, "updated", $"updated {before} -> {now}");
            }
        }

        // No climb within the window. Either already current, or the update is still downloading. We cannot
        // distinguish "no update" from "still in flight" without a Play status read (none reliable over adb),
        // so report up_to_date with a note; a later check re-confirms.
        var last = await ReadInstalledAsync(device, ct);
        logger.LogInformation("device check-update: {Id} android up_to_date installed={Ver} (no climb in {Max}x{Sec}s)",
            device.Id, last ?? "?", opts.PollAttempts, opts.PollSeconds);
        return new StoreCheckResult(true, before, last, false, false, "up_to_date",
            $"no newer version installed within {opts.PollAttempts}x{opts.PollSeconds}s (already current, or download in flight)");
    }

    // adb -s <serial> shell <DriveTemplate with {package}>. The template is a single shell command line; we
    // hand it to adb shell as one argument so the device's shell parses it.
    private string[] BuildDriveArgs(DeviceStoreTarget device)
    {
        var cmd = opts.DriveTemplate.Replace("{package}", device.Package);
        return ["-s", device.Target, "shell", cmd];
    }

    private async Task<string?> ReadInstalledAsync(DeviceStoreTarget device, CancellationToken ct)
    {
        var probe = await new AdbDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }
}
