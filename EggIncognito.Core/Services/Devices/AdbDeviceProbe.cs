namespace EggIncognito.Core.Services.Devices;

// Android status probe: `adb -s <serial> shell dumpsys package <pkg>` then parse versionName/Code.
// A non-zero exit or empty version means the device is not reachable (unplugged / adb offline).
public sealed class AdbDeviceProbe(IProcessRunner runner, string serial, string package) : IDeviceProbe
{
    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync("adb", ["-s", serial, "shell", "dumpsys", "package", package], ct);
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        var (app, build) = DeviceParsing.AndroidVersion(r.Stdout);
        if (build is null && app is null)
            return new DeviceProbeResult(false, null, null, "no version in dumpsys");

        return new DeviceProbeResult(true, app, build, null);
    }
}
