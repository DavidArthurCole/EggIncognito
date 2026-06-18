using EggIncognito.Core.Services.Devices;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Devices;

// iOS device-driven store check = drive the on-device App Store via the eggupdate tweak (ssh-touch the
// watched trigger file), then re-read the installed version to see whether it climbed. The device's App
// Store + its logged-in account do the work; the server only fires the trigger + polls. No version list:
// the App Store itself decides the target, so we detect ANY climb rather than matching a known version.
//
// Reuses the iOS ssh config the updater/puller use (DeviceUpdate:Ios:*). Never throws.
public sealed class IosStoreChecker(
    IProcessRunner runner, IConfiguration config, ILogger<IosStoreChecker> logger) : IDeviceStoreChecker
{
    public string Platform => "ios";

    public async Task<StoreCheckResult> CheckAndUpdateAsync(DeviceStoreTarget device, CancellationToken ct)
    {
        var before = await ReadInstalledAsync(device, ct);
        if (before is null)
            return new StoreCheckResult(false, null, null, false, false, "unreachable", "device unreachable or no version read");

        var s = config.GetSection("DeviceUpdate").GetSection("Ios");
        var host = s["SshHost"];
        var port = s["SshPort"] ?? "2222";
        var key = s["SshKeyPath"];
        var triggerPath = s["TriggerPath"] ?? "/var/mobile/eggupdate.trigger";
        var pollSeconds = s.GetValue("PollSeconds", 15);
        var pollAttempts = s.GetValue("PollAttempts", 24);

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key))
            return new StoreCheckResult(true, before, before, false, false, "error",
                "ios ssh not configured (DeviceUpdate:Ios:SshHost/SshKeyPath)");

        // Fire the eggupdate tweak: touch the watched file over ssh. The tweak drives StoreServices to install
        // any pending App Store update for egginc. No injection, no tap.
        logger.LogInformation("device check-update: {Id} ios firing eggupdate trigger", device.Id);
        var fire = await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", $"touch {triggerPath}"], ct);
        if (fire.ExitCode != 0)
            return new StoreCheckResult(true, before, before, false, false, "error",
                $"trigger ssh failed: {DeviceParsing.TrimNote(fire.Stderr + fire.Stdout)}");

        // Poll for ANY version climb (the App Store chose the target).
        for (var attempt = 0; attempt < pollAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct); }
            catch (OperationCanceledException) { break; }

            var now = await ReadInstalledAsync(device, ct);
            if (now is not null && DeviceProbeRunner.SemverCompare(now, before) > 0)
            {
                logger.LogInformation("device check-update: {Id} ios {Before} -> {After}", device.Id, before, now);
                return new StoreCheckResult(true, before, now, true, true, "updated", $"updated {before} -> {now}");
            }
        }

        var last = await ReadInstalledAsync(device, ct);
        return new StoreCheckResult(true, before, last, false, false, "up_to_date",
            $"no newer version within {pollAttempts}x{pollSeconds}s (already current, or App Store install in flight)");
    }

    private async Task<string?> ReadInstalledAsync(DeviceStoreTarget device, CancellationToken ct)
    {
        var probe = await new IosDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }
}
