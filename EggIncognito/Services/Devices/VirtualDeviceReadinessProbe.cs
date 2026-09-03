using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class VirtualDeviceReadinessProbe(
    IDeviceConnectionFactory connections,
    VirtualDeviceConfig config,
    IConfiguration configuration) {
    private const string IntegrityDetect =
        "ls -d /data/adb/modules/*integrity* 2>/dev/null; "
        + "grep -il integrity /data/adb/modules/*/module.prop 2>/dev/null";
    private const string SystemCaCerts = "/system/etc/security/cacerts/";

    public async Task<DeviceReadiness> ProbeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) {
            var na = new ReadinessCheck(false, "android only");
            return new DeviceReadiness(na, na, na, na, na, na);
        }

        if (connections.For(target) is not { } conn) {
            var no = new ReadinessCheck(false, "no connection");
            return new DeviceReadiness(no, no, no, no, no, no);
        }

        if (await OfflineAsync(conn, ct) is { } offline)
            return new DeviceReadiness(offline, offline, offline, offline, offline, offline);

        var root = await DeviceRoot.ProbeAsync(conn, ct);
        var installed = await InstalledAsync(conn, target.Package, ct);
        var play = await GooglePlayAsync(conn, ct);
        var rooted = RootedCheck(root);
        var integrity = await IntegrityAsync(conn, root, ct);
        var launched = await LaunchedAsync(conn, target.Package, ct);
        var ca = await CaptureCaAsync(conn, ct);
        return new DeviceReadiness(installed, ca, play, rooted, integrity, launched);
    }

    private static async Task<ReadinessCheck?> OfflineAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync("getprop sys.boot_completed", ct);
        if (r.ExitCode != 0 || r.Stdout.Trim().Length == 0) return new ReadinessCheck(false, "device unreachable");
        return r.Stdout.Trim() == "1" ? null : new ReadinessCheck(false, "device is booting");
    }

    private static async Task<ReadinessCheck> InstalledAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync($"pm path {package}", ct);
        return r.ExitCode == 0 && r.Stdout.Contains("package:", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not installed");
    }

    private async Task<ReadinessCheck> GooglePlayAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync($"pm list packages {config.GmsPackage}", ct);
        return r.Stdout.Contains("package:", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "no Play services, needs a gapps image");
    }

    private static ReadinessCheck RootedCheck(RootAccess root) =>
        root.Ok ? new ReadinessCheck(true) : new ReadinessCheck(false, root.Detail);

    private static async Task<ReadinessCheck> IntegrityAsync(IDeviceConnection conn, RootAccess root, CancellationToken ct) {
        var r = await conn.ShellAsync(root.Wrap(IntegrityDetect), ct);
        return r.Stdout.Trim().Length > 0
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "no module in /data/adb/modules");
    }

    private static async Task<ReadinessCheck> LaunchedAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync($"pidof {package}", ct);
        return r.Stdout.Trim().Length > 0
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not running");
    }

    private async Task<ReadinessCheck> CaptureCaAsync(IDeviceConnection conn, CancellationToken ct) {
        if (CaptureCaPath.AndroidTrustFile(configuration) is not { } file)
            return new ReadinessCheck(false, "no capture CA minted");

        var r = await conn.ShellAsync($"[ -s {SystemCaCerts}{file} ] && echo present", ct);
        return r.Stdout.Contains("present", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "not in the trust store");
    }
}
