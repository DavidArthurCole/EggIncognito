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
        foreach (var d in devices) await PushOneAsync(d, host, ct);
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

    // Force a fresh rinfo: note the last-seen timestamp, launch the egginc app so it phones home, then poll
    // the device's rinfo until it changes (newer LastSeen) or the timeout elapses. Returns the freshest
    // rinfo seen (may be the prior one on timeout). iOS launch = uiopen the app URL; Android = monkey launch.
    public async Task<DeviceRinfo?> ForceHarvestAsync(DeviceEntry d, TimeSpan timeout, CancellationToken ct)
    {
        var before = manager.Rinfo.Latest(d.Id);
        await LaunchAppAsync(d, ct);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            var now = manager.Rinfo.Latest(d.Id);
            if (now is not null && (before is null || now.LastSeen != before.LastSeen)) return now;
        }
        return manager.Rinfo.Latest(d.Id);
    }

    // Launch the egginc app to make it phone auxbrain. Best-effort: a failed launch just means the harvest
    // relies on the app's own next poll. iOS over ssh (uiopen), Android over adb (monkey).
    private async Task LaunchAppAsync(DeviceEntry d, CancellationToken ct)
    {
        try
        {
            if (string.Equals(d.Platform, "android", StringComparison.OrdinalIgnoreCase))
            {
                await runner.RunAsync("adb",
                    ["-s", d.Target, "shell", "monkey", "-p", d.Package, "-c", "android.intent.category.LAUNCHER", "1"], ct);
            }
            else if (string.Equals(d.Platform, "ios", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(config.IosSshHost) || string.IsNullOrEmpty(config.IosSshKeyPath)) return;
                await runner.RunAsync("ssh",
                    ["-p", config.IosSshPort, "-i", config.IosSshKeyPath, "-o", "StrictHostKeyChecking=no",
                     "-o", "BatchMode=yes", $"root@{config.IosSshHost}", $"uiopen {d.Package}://"], ct);
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "device capture: {Id} app launch failed (non-fatal)", d.Id); }
    }
}
