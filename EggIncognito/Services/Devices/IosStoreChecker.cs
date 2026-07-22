using EggIncognito.Core.Services.Devices;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services.Devices;


public sealed class IosStoreChecker(
    IProcessRunner runner, IConfiguration config, ILogger<IosStoreChecker> logger) : IDeviceStoreChecker {
    public string Platform => "ios";

    public async Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceStoreTarget device, CancellationToken ct, Action<string>? progress = null) {
        progress?.Invoke("reading installed version over usbmux…");
        var before = await ReadInstalledAsync(device, ct);
        if (before is null) {
            logger.LogInformation("device check-update: {Id} ios unreachable (no version read)", device.Id);
            return new StoreCheckResult(false, null, null, false, false, "unreachable", "device unreachable or no version read");
        }

        var s = config.GetSection("DeviceUpdate").GetSection("Ios");
        var host = s["SshHost"];
        var port = s["SshPort"] ?? "2222";
        var key = s["SshKeyPath"];
        var triggerPath = s["TriggerPath"] ?? "/var/mobile/eggupdate.trigger";
        var pollSeconds = s.GetValue("PollSeconds", 15);
        var pollAttempts = s.GetValue("PollAttempts", 24);

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key)) {
            logger.LogWarning("device check-update: {Id} ios error: ssh not configured", device.Id);
            return new StoreCheckResult(true, before, before, false, false, "error",
                "ios ssh not configured (DeviceUpdate:Ios:SshHost/SshKeyPath)");
        }



        progress?.Invoke($"installed {before}; launching App Store to prime session…");
        logger.LogInformation("device check-update: {Id} ios launching App Store + firing trigger", device.Id);
        await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", "killall -9 AppStore 2>/dev/null || true"], ct);
        try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { }
        await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", "uiopen itms-apps://itunes.apple.com/app/id993492744 || true"], ct);
        try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { }

        var fire = await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", $"touch {triggerPath}"], ct);
        if (fire.ExitCode != 0) {
            var note = $"trigger ssh failed: {DeviceParsing.TrimNote(fire.Stderr + fire.Stdout)}";
            logger.LogWarning("device check-update: {Id} ios error: {Note}", device.Id, note);
            return new StoreCheckResult(true, before, before, false, false, "error", note);
        }

        progress?.Invoke($"trigger fired; waiting for App Store to install (up to {pollAttempts * pollSeconds}s)…");
        try {
            for (var attempt = 0; attempt < pollAttempts; attempt++) {
                if (ct.IsCancellationRequested) break;
                try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct); } catch (OperationCanceledException) { break; }

                var now = await ReadInstalledAsync(device, ct);
                var n = attempt + 1;
                var elapsed = n * pollSeconds;
                logger.LogInformation("device check-update: {Id} ios poll {N}/{Max} installed={Ver}",
                    device.Id, n, pollAttempts, now ?? "?");
                if (now is not null && DeviceProbeRunner.SemverCompare(now, before) > 0) {
                    progress?.Invoke($"App Store installed {now} (was {before})");
                    logger.LogInformation("device check-update: {Id} ios climb {Before} -> {After}", device.Id, before, now);
                    return new StoreCheckResult(true, before, now, true, true, "updated", $"updated {before} -> {now}");
                }
                progress?.Invoke($"waiting for App Store install… {elapsed}s elapsed (no change yet)");
            }

            var last = await ReadInstalledAsync(device, ct);
            logger.LogInformation("device check-update: {Id} ios up_to_date installed={Ver} (no climb in {Max}x{Sec}s)",
                device.Id, last ?? "?", pollAttempts, pollSeconds);
            return new StoreCheckResult(true, before, last, false, false, "up_to_date",
                $"no update applied in {pollAttempts * pollSeconds}s (already current, or App Store install still in flight)");
        } finally {
            await SleepDeviceAsync(host!, port, key!, CancellationToken.None);
        }
    }



    private async Task SleepDeviceAsync(string host, string port, string key, CancellationToken ct) {
        try {
            await runner.RunAsync("ssh",
                ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{host}", "killall -9 AppStore 2>/dev/null || true"], ct);
        } catch (Exception ex) { logger.LogDebug(ex, "ios app-close best-effort failed"); }
    }

    private async Task<string?> ReadInstalledAsync(DeviceStoreTarget device, CancellationToken ct) {
        var probe = await new IosDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }
}
