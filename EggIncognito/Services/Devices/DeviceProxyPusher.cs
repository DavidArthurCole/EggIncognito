using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceProxyPusher(
    DeviceCaptureManager manager,
    DeviceCaptureConfig config,
    IProcessRunner runner,
    IDeviceConnectionFactory connections,
    IEnumerable<IDeviceProxyConfigurator> configurators,
    ILogger<DeviceProxyPusher> logger) {
    private readonly Dictionary<string, IDeviceProxyConfigurator> _byPlatform =
        configurators.ToDictionary(c => c.Platform, StringComparer.OrdinalIgnoreCase);

    private bool _warnedBridge;

    public string? HostIp => HostAddress.Resolve(config.HostIp);

    public async Task PushAllAsync(IReadOnlyList<DeviceEntry> devices, CancellationToken ct) {
        if (!config.Enabled) return;
        string? host = HostIp;
        if (string.IsNullOrEmpty(host)) {
            logger.LogWarning("device capture: cannot push proxy, host IP unresolved (set DeviceCapture:HostIp)");
            return;
        }

        if (!_warnedBridge && string.IsNullOrWhiteSpace(config.HostIp) && LooksLikeDockerBridge(host)) {
            _warnedBridge = true;
            logger.LogWarning(
                "device capture: auto-detected host IP {Host} looks like a docker bridge address - LAN devices " +
                "cannot reach it, so no traffic will be captured. Pin DeviceCapture:HostIp to the host's LAN IP.",
                host);
        }

        foreach (var d in devices) await PushOneAsync(d, host, ct);
    }


    internal static bool LooksLikeDockerBridge(string ip) {
        string[] p = ip.Split('.');
        return p.Length == 4 && p[0] == "172" && int.TryParse(p[1], out int b) && b >= 16 && b <= 31;
    }

    public async Task<(bool Ok, string? Note)> PushOneAsync(DeviceEntry d, string host, CancellationToken ct) {
        int port = manager.PortFor(d.Id);
        if (port == 0) return (false, "no capture listener for device");
        if (!_byPlatform.TryGetValue(d.Platform, out var cfg))
            return (false, $"no proxy configurator for {d.Platform}");

        (bool ok, string? note) =
            await cfg.SetProxyAsync(new DeviceProxyTarget(d.Id, d.Platform, d.Target), host, port, ct);
        if (ok) logger.LogInformation("device capture: {Id} proxy -> {Host}:{Port}", d.Id, host, port);
        else logger.LogWarning("device capture: {Id} proxy push failed: {Note}", d.Id, note);
        return (ok, note);
    }


    public async Task<DeviceRinfo?> ForceHarvestAsync(DeviceEntry d, TimeSpan timeout, CancellationToken ct) {
        var before = manager.Rinfo.Latest(d.Id);
        await RestartAppAsync(d, ct);

        var deadline = DateTimeOffset.UtcNow + timeout;
        DeviceRinfo? result = null;
        while (DateTimeOffset.UtcNow < deadline) {
            try {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            } catch (OperationCanceledException) {
                break;
            }

            var now = manager.Rinfo.Latest(d.Id);
            if (now is not null && (before is null || now.LastSeen != before.LastSeen)) {
                result = now;
                break;
            }
        }

        try {
            await LockDeviceAsync(d, ct);
        } catch (Exception ex) {
            logger.LogDebug(ex, "device {Id} relock failed (non-fatal)", d.Id);
        }

        return result ?? manager.Rinfo.Latest(d.Id);
    }


    public async Task<(bool Ok, string? Note)> LockDeviceAsync(DeviceEntry d, CancellationToken ct) {
        if (string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase)) {
            await runner.RunAsync("adb", ["-s", d.Target, "shell", "svc", "power", "stayon", "false"], ct);
            var r = await runner.RunAsync("adb", ["-s", d.Target, "shell", "input", "keyevent", "KEYCODE_SLEEP"], ct);
            return r.ExitCode == 0 ? (true, "locked") : (false, "lock failed");
        }

        if (string.Equals(d.Platform, "ios", StringComparison.OrdinalIgnoreCase)) {
            if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
                return (false, "ios ssh not configured");
            await IosKillAppAsync(ct);
            (bool ok, string? note) = await IosSendCmdAsync("lock", ct);
            return ok ? (true, "app killed + locked") : (false, $"lock failed: {note}");
        }

        return (false, $"no lock for platform {d.Platform}");
    }


    private async Task IosKillAppAsync(CancellationToken ct) {
        const string remote =
            "/bin/sh -c 'for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | " +
            "while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; echo killed'";
        if (connections.Ios() is { } conn) await conn.ShellAsync(remote, ct);
    }


    private async Task<bool?> IosLockstateAsync(CancellationToken ct) {
        if (connections.Ios() is not { } conn) return null;
        var r = await conn.ShellAsync("lockstate", ct);
        return r.Stdout.Contains("locked=1")
            ? true
            : r.Stdout.Contains("locked=0")
                ? false
                : r.ExitCode switch { 10 => true, 0 => false, _ => null };
    }


    private async Task<(bool Ok, string? Note)> IosSendCmdAsync(string cmd, CancellationToken ct) {
        if (connections.Ios() is not { } conn) return (false, "ios ssh not configured");
        string remote = $"/bin/sh -c 'printf %s {cmd} > /tmp/ehp.cmd; chmod 666 /tmp/ehp.cmd; echo sent'";
        var r = await conn.ShellAsync(remote, ct);
        return r.ExitCode == 0
            ? (true, null)
            : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    private async Task<bool> IosEnsureUnlockedAsync(CancellationToken ct, int maxTries = 3) {
        for (int i = 0; i < maxTries; i++) {
            bool? locked = await IosLockstateAsync(ct);
            if (locked == false) return true;
            await IosSendCmdAsync("unlock", ct);
            try {
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
            } catch (OperationCanceledException) {
                return false;
            }
        }

        return await IosLockstateAsync(ct) == false;
    }


    public async Task<(bool Ok, string? Note)> RestartAppAsync(DeviceEntry d, CancellationToken ct) {
        try {
            if (string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase)) {
                await runner.RunAsync("adb", ["-s", d.Target, "shell", "input", "keyevent", "KEYCODE_WAKEUP"], ct);
                await runner.RunAsync("adb", ["-s", d.Target, "shell", "wm", "dismiss-keyguard"], ct);
                await runner.RunAsync("adb", ["-s", d.Target, "shell", "svc", "power", "stayon", "true"], ct);
                var stop = await runner.RunAsync("adb", ["-s", d.Target, "shell", "am", "force-stop", d.Package], ct);
                if (stop.ExitCode != 0) {
                    logger.LogWarning("device capture: {Id} force-stop failed: {Note}",
                        d.Id, DeviceParsing.TrimNote(stop.Stderr + stop.Stdout));
                }

                var launch = await runner.RunAsync("adb",
                    ["-s", d.Target, "shell", "monkey", "-p", d.Package, "-c", "android.intent.category.LAUNCHER", "1"],
                    ct);
                bool ok = launch.ExitCode == 0;
                logger.LogInformation("device capture: {Id} app restarted (launch ok={Ok})", d.Id, ok);
                return ok ? (true, "restarted") : (false, "launch failed");
            }

            if (string.Equals(d.Platform, "ios", StringComparison.OrdinalIgnoreCase)) {
                if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath))
                    return (false, "ios ssh not configured");
                string bundle = d.Package;
                string? proc = string.IsNullOrEmpty(config.IosAppProcessName) ? "Egg, Inc." : config.IosAppProcessName;

                if (string.IsNullOrEmpty(config.IosRestartCommand)) {
                    bool unlocked = await IosEnsureUnlockedAsync(ct);
                    if (!unlocked)
                        logger.LogWarning("device capture: {Id} could not confirm unlock; launching anyway", d.Id);
                }

                string remote = string.IsNullOrEmpty(config.IosRestartCommand)
                    ? "/bin/sh -c '" +
                      "for p in $(ps ax 2>/dev/null | grep -i egg | grep -v grep | while read pid rest; do echo $pid; done); do kill -9 $p 2>/dev/null; done; sleep 1; " +
                      $"uiopen --bundleid {bundle} 2>&1 | sed \"s/^/diag uiopen: /\"; " +
                      "sleep 3; echo diag ps-after:; " +
                      "if ps ax 2>/dev/null | grep -i egg | grep -v grep; then echo \"diag RESULT: running\"; else echo \"diag RESULT: NOT running\"; fi" +
                      "'"
                    : config.IosRestartCommand.Replace("{bundle}", bundle).Replace("{proc}", proc);
                if (connections.Ios() is not { } conn) return (false, "ios ssh not configured");
                var r = await conn.ShellAsync(remote, ct);
                string diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
                bool launched = r.Stdout.Contains("diag RESULT: running");
                logger.LogInformation("device capture: {Id} ios restart (running-after={Ok}): {Diag}", d.Id, launched,
                    diag);
                return r.ExitCode == 0
                    ? (true, $"{(launched ? "running" : "NOT running - see diag")}: {diag}")
                    : (false, diag);
            }

            return (false, $"no restart for platform {d.Platform}");
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} app restart failed (non-fatal)", d.Id);
            return (false, ex.Message);
        }
    }
}
