using EggIncognito.Core.Services.Devices;
using EggIncognito.Models.Devices;

namespace EggIncognito.Services.Devices;

public sealed class VirtualDeviceReadinessProbe(
    IDeviceConnectionFactory connections,
    VirtualDeviceConfig config) {
    private const string IntegrityDetect =
        "su -c 'ls -d /data/adb/modules/*integrity* 2>/dev/null; "
        + "grep -il integrity /data/adb/modules/*/module.prop 2>/dev/null'";
    private const string SystemCaCerts = "/system/etc/security/cacerts/";

    public async Task<DeviceReadiness> ProbeAsync(DeviceTarget target, CancellationToken ct) {
        if (!Platforms.Matches(target.Platform, Platforms.Android)) {
            var na = new ReadinessCheck(false, "readiness probing is android-only");
            return new DeviceReadiness(na, na, na, na, na, na);
        }

        if (connections.For(target) is not { } conn) {
            var no = new ReadinessCheck(false, "no connection for this device");
            return new DeviceReadiness(no, no, no, no, no, no);
        }

        var installed = await InstalledAsync(conn, target.Package, ct);
        var play = await GooglePlayAsync(conn, ct);
        var rooted = await RootedAsync(conn, ct);
        var integrity = await IntegrityAsync(conn, ct);
        var launched = await LaunchedAsync(conn, target.Package, ct);
        var ca = await CaptureCaAsync(conn, ct);
        return new DeviceReadiness(installed, ca, play, rooted, integrity, launched);
    }

    private static async Task<ReadinessCheck> InstalledAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync($"pm path {package}", ct);
        return r.ExitCode == 0 && r.Stdout.Contains("package:", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, $"{package} is not installed");
    }

    private async Task<ReadinessCheck> GooglePlayAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync($"pm list packages {config.GmsPackage}", ct);
        return r.Stdout.Contains("package:", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, $"no {config.GmsPackage}; image lacks Google Play (use the gapps image)");
    }

    private static async Task<ReadinessCheck> RootedAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync("su -c id", ct);
        return r.Stdout.Contains("uid=0", StringComparison.Ordinal)
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "su did not report uid=0");
    }

    private static async Task<ReadinessCheck> IntegrityAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync(IntegrityDetect, ct);
        return r.Stdout.Trim().Length > 0
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "Integrity-Box module not found under /data/adb/modules");
    }

    private static async Task<ReadinessCheck> LaunchedAsync(IDeviceConnection conn, string package, CancellationToken ct) {
        var r = await conn.ShellAsync($"pidof {package}", ct);
        return r.Stdout.Trim().Length > 0
            ? new ReadinessCheck(true)
            : new ReadinessCheck(false, "app is not running");
    }

    private static async Task<ReadinessCheck> CaptureCaAsync(IDeviceConnection conn, CancellationToken ct) {
        var r = await conn.ShellAsync($"ls {SystemCaCerts}", ct);
        return r.Stdout.Trim().Length > 0
            ? new ReadinessCheck(true, "system cacerts present (capture CA not individually verified)")
            : new ReadinessCheck(false, "could not read the system trust store");
    }
}
