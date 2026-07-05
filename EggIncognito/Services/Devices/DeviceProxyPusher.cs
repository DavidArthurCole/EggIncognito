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
        DeviceRinfo? result = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            var now = manager.Rinfo.Latest(d.Id);
            if (now is not null && (before is null || now.LastSeen != before.LastSeen)) { result = now; break; }
        }
        // Re-lock the device when done: the farm phones sit LOCKED (screen off) to save power; the harvest
        // woke + unlocked them, so put them back. Best-effort, never fails the harvest.
        try { await LockDeviceAsync(d, ct); } catch (Exception ex) { logger.LogDebug(ex, "device {Id} relock failed (non-fatal)", d.Id); }
        return result ?? manager.Rinfo.Latest(d.Id);
    }

    // Return the device to its low-power locked resting state after a capture. Android: the SLEEP keyevent
    // (locks + screen off). iOS: KILL the egginc app (so it is not left foregrounded on the game screen) then
    // send the `lock` cmd - the backboardd EggHomePress dylib taps the power key (consumer 0x30, ONE short
    // tap = sleep+lock, never the power-off menu), so the device returns to lockstate=1 with the screen off.
    public async Task<(bool Ok, string? Note)> LockDeviceAsync(DeviceEntry d, CancellationToken ct)
    {
        if (string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase))
        {
            // Drop the stayon hold the harvest set (else SLEEP cannot keep the screen off), then sleep+lock.
            await runner.RunAsync("adb", ["-s", d.Target, "shell", "svc", "power", "stayon", "false"], ct);
            var r = await runner.RunAsync("adb", ["-s", d.Target, "shell", "input", "keyevent", "KEYCODE_SLEEP"], ct);
            return r.ExitCode == 0 ? (true, "locked") : (false, "lock failed");
        }
        if (string.Equals(d.Platform, "ios", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
                return (false, "ios ssh not configured");
            await IosKillAppAsync(ct); // leave the home screen, not the game, before locking
            var (ok, note) = await IosSendCmdAsync("lock", ct);
            return ok ? (true, "app killed + locked") : (false, $"lock failed: {note}");
        }
        return (false, $"no lock for platform {d.Platform}");
    }

    // Kill the egginc app over ssh (pure-sh PID parse, no awk). Used before locking so the device is left on
    // the home screen, and before a fresh launch so a suspended app cannot just resume without re-hitting
    // auxbrain. Best-effort: a no-op if the app is not running.
    private async Task IosKillAppAsync(CancellationToken ct)
    {
        const string remote =
            "/bin/sh -c 'for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | " +
            "while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; echo killed'";
        await runner.RunAsync("ssh",
            ["-p", config.IosSshPort, "-i", config.IosSshKeyPath!, "-o", "StrictHostKeyChecking=no",
             "-o", "BatchMode=yes", $"root@{config.IosSshHost}", remote], ct);
    }

    // iOS unlock primitives. Server is the brain; the backboardd EggHomePress dylib is a dumb one-shot
    // executor. The dylib NEVER reads lock state and NEVER retries - that stale in-dylib read caused an
    // over-press spam loop. Instead: the server reads the headless `lockstate` CLI (exit 10=locked, 0=unlocked),
    // decides, and writes ONE cmd to /tmp/ehp.cmd (chmod 666 so the mobile-uid dylib can truncate it after
    // consuming). All retry lives here, against the accurate oracle.

    // Read the device lock state via the `lockstate` CLI. Returns true=locked, false=unlocked, null=unknown.
    private async Task<bool?> IosLockstateAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync("ssh",
            ["-p", config.IosSshPort, "-i", config.IosSshKeyPath!, "-o", "StrictHostKeyChecking=no",
             "-o", "BatchMode=yes", $"root@{config.IosSshHost}", "lockstate"], ct);
        // lockstate prints "locked=N passcode=M" and exits 10 (locked) / 0 (unlocked).
        if (r.Stdout.Contains("locked=1")) return true;
        if (r.Stdout.Contains("locked=0")) return false;
        return r.ExitCode switch { 10 => true, 0 => false, _ => (bool?)null };
    }

    // Write one command to the dylib's trigger file, world-writable so the mobile-uid dylib can truncate it
    // after consuming (consume = truncate-to-empty; empty content is ignored = exactly one action per write).
    private async Task<(bool Ok, string? Note)> IosSendCmdAsync(string cmd, CancellationToken ct)
    {
        var remote = $"/bin/sh -c 'printf %s {cmd} > /tmp/ehp.cmd; chmod 666 /tmp/ehp.cmd; echo sent'";
        var r = await runner.RunAsync("ssh",
            ["-p", config.IosSshPort, "-i", config.IosSshKeyPath!, "-o", "StrictHostKeyChecking=no",
             "-o", "BatchMode=yes", $"root@{config.IosSshHost}", remote], ct);
        return r.ExitCode == 0 ? (true, null)
                               : (false, EggIncognito.Core.Services.Devices.DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    // Ensure the iOS device is unlocked: read state, and while locked send one `unlock` cmd and re-check, up
    // to a few tries. The dylib's `unlock` = BKS wake + two consumer Menu(0x40) presses (iOS-16 passcode-free
    // lockscreen: first press raises it, second dismisses to home). Server-side retry against `lockstate`
    // (the accurate oracle) replaces the buggy in-dylib loop. Returns true once unlocked.
    private async Task<bool> IosEnsureUnlockedAsync(CancellationToken ct, int maxTries = 3)
    {
        for (var i = 0; i < maxTries; i++)
        {
            var locked = await IosLockstateAsync(ct);
            if (locked == false) return true; // confirmed unlocked
            await IosSendCmdAsync("unlock", ct);
            try { await Task.Delay(TimeSpan.FromSeconds(4), ct); } catch (OperationCanceledException) { return false; }
        }
        return await IosLockstateAsync(ct) == false;
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
                // The farm phone rests DOZING (screen off). A dozing device throttles the app so it never
                // completes its auxbrain launch call (the "not reaching auxbrain" symptom: other hosts connect
                // but auxbrain never does). WAKE + dismiss the keyguard + hold the screen on, THEN launch, so
                // the app runs at full speed and phones home. LockDeviceAsync drops stayon + sleeps afterward.
                await runner.RunAsync("adb", ["-s", d.Target, "shell", "input", "keyevent", "KEYCODE_WAKEUP"], ct);
                await runner.RunAsync("adb", ["-s", d.Target, "shell", "wm", "dismiss-keyguard"], ct);
                await runner.RunAsync("adb", ["-s", d.Target, "shell", "svc", "power", "stayon", "true"], ct);
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
                var bundle = d.Package; // com.auxbrain.egginc
                var proc = string.IsNullOrEmpty(config.IosAppProcessName) ? "Egg, Inc." : config.IosAppProcessName;
                // The farm phone rests LOCKED (screen off, no passcode) to save power. A locked iOS app launches
                // SUSPENDED and never phones auxbrain, so WAKE + UNLOCK first. The server reads `lockstate` and
                // drives the dumb backboardd dylib until unlocked (IosEnsureUnlockedAsync) - no in-dylib state
                // loop (that was the over-press spam bug). Skip the whole unlock/launch shell when overridden.
                if (string.IsNullOrEmpty(config.IosRestartCommand))
                {
                    var unlocked = await IosEnsureUnlockedAsync(ct);
                    if (!unlocked)
                        logger.LogWarning("device capture: {Id} could not confirm unlock; launching anyway", d.Id);
                }
                // /bin/sh cold-launch with diagnostics. A suspended iOS app must be KILLED before relaunch or it
                // just resumes without a fresh auxbrain call. Kill by PID (pure-sh parse, no awk), then cold-
                // launch by bundle id via the Procursus `uiopen --bundleid` flag (routes through
                // LSApplicationWorkspace, the one method that works over root ssh - `open` is SIGKILL'd,
                // SBSLaunchApplicationWithIdentifier hits FrontBoard cross-domain err 3, and EI has no URL
                // scheme). Override the whole command via DeviceCapture:Ios:RestartCommand.
                var remote = string.IsNullOrEmpty(config.IosRestartCommand)
                    ? "/bin/sh -c '" +
                      "for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; sleep 1; " +
                      $"uiopen --bundleid {bundle} 2>&1 | sed \"s/^/diag uiopen: /\"; " +
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
