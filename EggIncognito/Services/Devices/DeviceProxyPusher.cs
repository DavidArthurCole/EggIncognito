using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceProxyPusher(
    DeviceCaptureManager manager,
    DeviceCaptureConfig config,
    IDevicePlatforms platforms,
    ILogger<DeviceProxyPusher> logger) {
    private bool _warnedBridge;

    public string? HostIp => HostAddress.Resolve(config.HostIp);

    private static DeviceTarget TargetOf(DeviceEntry d) => new(d.Id, d.Platform, d.Target, d.Package);

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

        var res = await platforms.For(d.Platform).SetProxyAsync(TargetOf(d), host, port, ct);
        if (res.Ok) logger.LogInformation("device capture: {Id} proxy -> {Host}:{Port}", d.Id, host, port);
        else logger.LogWarning("device capture: {Id} proxy push failed: {Note}", d.Id, res.Note);
        return (res.Ok, res.Note);
    }
}
