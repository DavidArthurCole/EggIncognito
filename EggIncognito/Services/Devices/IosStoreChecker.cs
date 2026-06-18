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

    public async Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceStoreTarget device, CancellationToken ct, Action<string>? progress = null)
    {
        progress?.Invoke("reading installed version over usbmux…");
        var before = await ReadInstalledAsync(device, ct);
        if (before is null)
        {
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

        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(key))
        {
            logger.LogWarning("device check-update: {Id} ios error: ssh not configured", device.Id);
            return new StoreCheckResult(true, before, before, false, false, "error",
                "ios ssh not configured (DeviceUpdate:Ios:SshHost/SshKeyPath)");
        }

        // Fire the eggupdate tweak. The proven path (2026-06-18): the tweak builds an SSPurchase standard-
        // redownload for egginc (adam-id 993492744) and starts an SSPurchaseRequest; storedownloadd then
        // downloads + installs the latest store version headlessly. No injection, no tap, no entitlement.
        // The SSPurchase needs the App Store app's authenticated account session live, so launch it first
        // (uiopen) to prime the session, then touch the kqueue-watched trigger file. The tweak's %ctor also
        // runs the flow on launch if /var/mobile/eggupdate.armed exists; touching the trigger covers the case
        // where the app is already alive.
        progress?.Invoke($"installed {before}; launching App Store to prime session…");
        logger.LogInformation("device check-update: {Id} ios launching App Store + firing trigger", device.Id);
        // Kill any stale App Store process FIRST. A headless uiopen leaves the app SIGKILLed-but-unreaped
        // (state `?s`); a later uiopen sees it "already running" and does NOT spawn a fresh process, so the
        // armed eggupdate dylib never loads + the trigger no-ops. killall forces a clean relaunch that loads
        // the dylib. (Proven failure mode 2026-06-18: prod fire no-op'd against a 06:28 zombie.)
        await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", "killall -9 AppStore 2>/dev/null || true"], ct);
        try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { }
        await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", "uiopen itms-apps://itunes.apple.com/app/id993492744 || true"], ct);
        // settle so the App Store session authenticates + the dylib loads before we trigger the purchase
        try { await Task.Delay(TimeSpan.FromSeconds(3), ct); } catch (OperationCanceledException) { }

        var fire = await runner.RunAsync("ssh",
            ["-p", port, "-i", key, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{host}", $"touch {triggerPath}"], ct);
        if (fire.ExitCode != 0)
        {
            var note = $"trigger ssh failed: {DeviceParsing.TrimNote(fire.Stderr + fire.Stdout)}";
            logger.LogWarning("device check-update: {Id} ios error: {Note}", device.Id, note);
            return new StoreCheckResult(true, before, before, false, false, "error", note);
        }

        // Poll for ANY version climb (the App Store chose the target). The progress line reports WHAT the
        // server is doing + elapsed time, not the unchanged installed version (which the UI already shows on
        // the row). A climb is announced the instant it is seen.
        progress?.Invoke($"trigger fired; waiting for App Store to install (up to {pollAttempts * pollSeconds}s)…");
        for (var attempt = 0; attempt < pollAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct); }
            catch (OperationCanceledException) { break; }

            var now = await ReadInstalledAsync(device, ct);
            var n = attempt + 1;
            var elapsed = n * pollSeconds;
            logger.LogInformation("device check-update: {Id} ios poll {N}/{Max} installed={Ver}",
                device.Id, n, pollAttempts, now ?? "?");
            if (now is not null && DeviceProbeRunner.SemverCompare(now, before) > 0)
            {
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
    }

    private async Task<string?> ReadInstalledAsync(DeviceStoreTarget device, CancellationToken ct)
    {
        var probe = await new IosDeviceProbe(runner, device.Target, device.Package).ProbeAsync(ct);
        return probe.InstalledAppVersion;
    }
}
