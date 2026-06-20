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
                // Kill the running app process (by its executable name) then relaunch via the URL scheme. The
                // process name is app-specific (config; default "Egg, Inc." for the egginc binary).
                var proc = string.IsNullOrEmpty(config.IosAppProcessName) ? "Egg, Inc." : config.IosAppProcessName;
                var remote = $"killall -9 \"{proc}\" 2>/dev/null; sleep 1; uiopen {d.Package}://";
                var r = await runner.RunAsync("ssh",
                    ["-p", config.IosSshPort, "-i", config.IosSshKeyPath, "-o", "StrictHostKeyChecking=no",
                     "-o", "BatchMode=yes", $"root@{config.IosSshHost}", remote], ct);
                var ok = r.ExitCode == 0;
                logger.LogInformation("device capture: {Id} app restarted over ssh (ok={Ok})", d.Id, ok);
                return ok ? (true, "restarted")
                          : (false, EggIncognito.Core.Services.Devices.DeviceParsing.TrimNote(r.Stderr + r.Stdout));
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
