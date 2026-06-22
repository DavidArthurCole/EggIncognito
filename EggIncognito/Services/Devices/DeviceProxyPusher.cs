using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Drives each declared device's system HTTP proxy to its dedicated capture listener, and forces a fresh
// rinfo harvest on demand. Called by the probe loop + on capture start, so the proxy setting self-heals
// after a device reboot or server restart (idempotent re-push). Forcing a harvest = launch the egginc app
// (it phones auxbrain on launch) then wait for the device's rinfo to update.
//
// Resolves: host IP (config override or auto-detect, never a hard-coded host name), per-device port (from
// the manager), and the right platform configurator. Never throws: every push/trigger returns a result.
public sealed class DeviceProxyPusher(
    DeviceCaptureManager manager,
    DeviceCaptureConfig config,
    IProcessRunner runner,
    IEnumerable<IDeviceProxyConfigurator> configurators,
    ILogger<DeviceProxyPusher> logger)
{
    private readonly Dictionary<string, IDeviceProxyConfigurator> _byPlatform =
        configurators.ToDictionary(c => c.Platform, StringComparer.OrdinalIgnoreCase);

    // Resolve the address devices dial back to. Config override wins; else auto-detect the primary LAN IPv4.
    public string? HostIp => HostAddress.Resolve(config.HostIp);

    private bool _warnedBridge;

    // Push every declared device's proxy to its capture port. Best-effort per device.
    public async Task PushAllAsync(IReadOnlyList<DeviceEntry> devices, CancellationToken ct)
    {
        if (!config.Enabled) return;
        var host = HostIp;
        if (string.IsNullOrEmpty(host))
        {
            logger.LogWarning("device capture: cannot push proxy, host IP unresolved (set DeviceCapture:HostIp)");
            return;
        }
        // Containerized hosts auto-detect the docker BRIDGE IP (172.17-31.x), which a LAN device cannot reach -
        // the device's traffic then never arrives and capture sees 0 flows. If HostIp was auto-detected (not
        // pinned) and looks like a bridge address, warn once: pin DeviceCapture:HostIp to the host's LAN IP.
        if (!_warnedBridge && string.IsNullOrWhiteSpace(config.HostIp) && LooksLikeDockerBridge(host))
        {
            _warnedBridge = true;
            logger.LogWarning(
                "device capture: auto-detected host IP {Host} looks like a docker bridge address - LAN devices " +
                "cannot reach it, so no traffic will be captured. Pin DeviceCapture:HostIp to the host's LAN IP.",
                host);
        }
        foreach (var d in devices) await PushOneAsync(d, host, ct);
    }

    // 172.17.0.0/16 (default docker bridge) + the wider 172.16/12 docker-compose range. Not authoritative
    // (172.16/12 is a legit LAN range), just a heuristic to flag the common containerized misconfig.
    internal static bool LooksLikeDockerBridge(string ip)
    {
        var p = ip.Split('.');
        return p.Length == 4 && p[0] == "172" && int.TryParse(p[1], out var b) && b >= 16 && b <= 31;
    }

    public async Task<(bool Ok, string? Note)> PushOneAsync(DeviceEntry d, string host, CancellationToken ct)
    {
        var port = manager.PortFor(d.Id);
        if (port == 0) return (false, "no capture listener for device");
        if (!_byPlatform.TryGetValue(d.Platform, out var cfg)) return (false, $"no proxy configurator for {d.Platform}");

        var (ok, note) = await cfg.SetProxyAsync(new DeviceProxyTarget(d.Id, d.Platform, d.Target), host, port, ct);
        if (ok) logger.LogInformation("device capture: {Id} proxy -> {Host}:{Port}", d.Id, host, port);
        else logger.LogWarning("device capture: {Id} proxy push failed: {Note}", d.Id, note);
        return (ok, note);
    }

    // Force a fresh rinfo: note the last-seen timestamp, FORCE-RESTART the egginc app (kill then launch) so
    // it makes a fresh launch API call to auxbrain, then poll the device's rinfo until it changes (newer
    // LastSeen) or the timeout elapses. A force-stop is required: a backgrounded/idle app does NOT re-hit
    // auxbrain on a plain foreground, so without the kill the harvest sees nothing (the bug behind "not
    // reaching auxbrain" - the game was simply not phoning home). Returns the freshest rinfo seen.
    public async Task<DeviceRinfo?> ForceHarvestAsync(DeviceEntry d, TimeSpan timeout, CancellationToken ct)
    {
        var before = manager.Rinfo.Latest(d.Id);
        await RestartAppAsync(d, ct);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            var now = manager.Rinfo.Latest(d.Id);
            if (now is not null && (before is null || now.LastSeen != before.LastSeen)) return now;
        }
        return manager.Rinfo.Latest(d.Id);
    }

    // Force-stop the egginc app then relaunch it, so it makes a fresh launch request to auxbrain (an idle
    // foreground does not re-authenticate). Public so a "force restart" button can trigger a capture on
    // demand. Best-effort + logged. Android: `am force-stop` + monkey launch. iOS: kill the app process by
    // name over ssh then uiopen the URL scheme.
    public async Task<(bool Ok, string? Note)> RestartAppAsync(DeviceEntry d, CancellationToken ct)
    {
        try
        {
            if (string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase))
            {
                var stop = await runner.RunAsync("adb", ["-s", d.Target, "shell", "am", "force-stop", d.Package], ct);
                if (stop.ExitCode != 0)
                    logger.LogWarning("device capture: {Id} force-stop failed: {Note}",
                        d.Id, EggIncognito.Core.Services.Devices.DeviceParsing.TrimNote(stop.Stderr + stop.Stdout));
                var launch = await runner.RunAsync("adb",
                    ["-s", d.Target, "shell", "monkey", "-p", d.Package, "-c", "android.intent.category.LAUNCHER", "1"], ct);
                var ok = launch.ExitCode == 0;
                logger.LogInformation("device capture: {Id} app restarted (launch ok={Ok})", d.Id, ok);
                return ok ? (true, "restarted") : (false, "launch failed");
            }
            if (string.Equals(d.Platform, "ios", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
                    return (false, "ios ssh not configured");
                // Jailbroken cold-launch with diagnostics. A backgrounded iOS app stays suspended; uiopen just
                // resumes it (no fresh auxbrain call), so we must KILL it first. The executable name is app-
                // specific, so we (1) report what egg-ish processes are running, (2) kill by the configured
                // name AND by anything matching the bundle, (3) try several launch methods (open <bundle> from
                // Procursus, then uiopen <bundle>:// scheme). Every step echoes `diag:` so the note shows what
                // worked. Override the whole command via DeviceCapture:Ios:RestartCommand if a method is wrong.
                var bundle = d.Package; // com.auxbrain.egginc
                var proc = string.IsNullOrEmpty(config.IosAppProcessName) ? "Egg, Inc." : config.IosAppProcessName;
                // /bin/sh (not the login zsh). A suspended iOS app must be KILLED before relaunch or it just
                // resumes without a fresh auxbrain call. EI registers no URL scheme, so cold-launch BY BUNDLE
                // ID via the Procursus `open` CLI (`open <bundleid>`, installed on this phone). Kill by PID
                // (pure-sh parse, no awk), then `open`, then report whether it is running after. Override the
                // whole command via DeviceCapture:Ios:RestartCommand.
                var remote = string.IsNullOrEmpty(config.IosRestartCommand)
                    ? "/bin/sh -c '" +
                      "for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; sleep 1; " +
                      // `open <bundle>` makes an XPC request to SpringBoard then exits. Run straight over ssh it
                      // got SIGKILL'd on session teardown (err 'Killed: 9') BEFORE the request landed; fully
                      // detached (setsid) it survived but the app still did not come up. Run it FOREGROUND and
                      // hold the ssh session open a beat so the XPC handoff completes before the shell exits.
                      $"if command -v open >/dev/null 2>&1; then open {bundle} 2>&1 | sed \"s/^/diag open: /\"; sleep 2; " +
                      $"else echo \"diag open: MISSING - apt install com.conradkramer.open\"; uiopen {bundle}:// 2>&1 | sed \"s/^/diag uiopen-fallback: /\"; fi; " +
                      "sleep 3; echo diag ps-after:; " +
                      "if ps ax 2>/dev/null | grep -i egg | grep -v grep; then echo \"diag RESULT: running\"; else echo \"diag RESULT: NOT running\"; fi" +
                      "'"
                    : config.IosRestartCommand.Replace("{bundle}", bundle).Replace("{proc}", proc);
                var r = await runner.RunAsync("ssh",
                    ["-p", config.IosSshPort, "-i", config.IosSshKeyPath, "-o", "StrictHostKeyChecking=no",
                     "-o", "BatchMode=yes", $"root@{config.IosSshHost}", remote], ct);
                var diag = EggIncognito.Core.Services.Devices.DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
                var launched = r.Stdout.Contains("diag RESULT: running");
                logger.LogInformation("device capture: {Id} ios restart (running-after={Ok}): {Diag}", d.Id, launched, diag);
                return r.ExitCode == 0 ? (true, $"{(launched ? "running" : "NOT running - see diag")}: {diag}")
                                       : (false, diag);
            }
            return (false, $"no restart for platform {d.Platform}");
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "device capture: {Id} app restart failed (non-fatal)", d.Id);
            return (false, ex.Message);
        }
    }
}
