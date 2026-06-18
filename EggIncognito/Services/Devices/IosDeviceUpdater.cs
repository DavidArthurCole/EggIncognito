using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Devices;

// iOS zero-touch update via the eggupdate.dylib SpringBoard tweak (ios-tweak/eggupdate). The tweak drives
// the phone's EXISTING, trusted StoreServices session (no GSA/Anisette re-auth) to install the latest App
// Store build. We never inject anything: the tweak watches a file, and we fire it by ssh-touching that file.
//
// Flow: ssh to the phone -> `touch <TriggerPath>` -> the tweak runs the two-phase StoreServices update
// async in SpringBoard/storedownloadd -> we poll the installed version over usbmux (ideviceinstaller, safe,
// jailbreak-independent) until it reaches the target or we time out. Never throws; failures are reported.
//
// Config (DeviceUpdate:Ios:*, default OFF): SshHost, SshPort (2222), SshKeyPath, TriggerPath
// (/var/mobile/eggupdate.trigger), PollSeconds (15), PollAttempts (24 => ~6 min). device.Target = UDID,
// device.Package = bundle id. Gated by DeviceUpdate:Ios:Enabled via RealDeviceUpgrader.
public sealed class IosDeviceUpdater(
    IProcessRunner runner, IConfiguration config, ILogger<IosDeviceUpdater> logger) : IDeviceUpdater
{
    public async Task<DeviceUpdateOutcome> UpdateAsync(Device device, string targetAppVersion, CancellationToken ct)
    {
        var from = await ReadInstalledAsync(device, ct);
        if (from == targetAppVersion)
            return new DeviceUpdateOutcome(false, true, from, targetAppVersion, "already current");

        var s = config.GetSection("DeviceUpdate").GetSection("Ios");
        var host = s["SshHost"];
        var port = s["SshPort"] ?? "2222";
        var key = s["SshKeyPath"];
        // /var/mobile (not /var/root): SpringBoard runs as user `mobile` and cannot write /var/root.
        var triggerPath = s["TriggerPath"] ?? "/var/mobile/eggupdate.trigger";
        var pollSeconds = s.GetValue("PollSeconds", 15);
        var pollAttempts = s.GetValue("PollAttempts", 24);

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key))
        {
            logger.LogInformation("device update: {Id} ios not configured (ssh host/key unset)", device.Id);
            return new DeviceUpdateOutcome(false, false, from, targetAppVersion, "ios auto-update not configured");
        }

        // Fire the tweak's trigger: touch the watched file over ssh. No injection, no on-device tap.
        logger.LogInformation("device update: {Id} ios {From} -> {To}: firing eggupdate trigger",
            device.Id, from, targetAppVersion);
        var fire = await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", $"touch {triggerPath}"], ct);
        if (fire.ExitCode != 0)
            return new DeviceUpdateOutcome(false, false, from, targetAppVersion,
                $"trigger ssh failed: {Trim(fire.Stderr + fire.Stdout)}");

        // Poll installed version over usbmux. The App Store update is async (storedownloadd), so we wait.
        for (var attempt = 0; attempt < pollAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct); }
            catch (OperationCanceledException) { break; }

            var now = await ReadInstalledAsync(device, ct);
            if (now == targetAppVersion)
            {
                logger.LogInformation("device update: {Id} ios verified {Now} after {N} polls",
                    device.Id, now, attempt + 1);
                return new DeviceUpdateOutcome(true, true, from, now, "updated");
            }
        }

        // Trigger fired but the version had not climbed within the window. The download may still be in
        // flight; a later probe re-checks. Report Started/unverified so the caller logs a "failed" attempt
        // (not a silent success) and the next poll can re-verify.
        var last = await ReadInstalledAsync(device, ct);
        logger.LogWarning("device update: {Id} ios still {Last} (target {Target}) after poll window",
            device.Id, last, targetAppVersion);
        return new DeviceUpdateOutcome(true, false, from, last,
            $"trigger fired; version still {last} after {pollAttempts}x{pollSeconds}s (download may be in flight)");
    }

    private async Task<string?> ReadInstalledAsync(Device device, CancellationToken ct)
    {
        var probe = await new IosDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }

    private static string Trim(string s) => s.Trim() is { Length: > 200 } t ? t[..200] : s.Trim();
}
