namespace EggIncognito.Core.Services.Devices;

// Points a rooted Android device's global HTTP proxy at the capture listener via adb. The device dials
// the capture host back over the LAN, so its auxbrain traffic is decrypted + harvested. Rooted, so the
// global proxy setting + CA trust are settable without a tap.
//
// adb reaches the device through the host's adb server (ADB_SERVER_SOCKET), same as the probes. device.Target
// is the adb serial. Idempotent: `settings put` overwrites; re-setting the same value is harmless. Never
// throws: a non-zero adb exit returns (false, note).
public sealed class AdbProxyConfigurator(IProcessRunner runner) : IDeviceProxyConfigurator
{
    public string Platform => "android";

    public async Task<(bool Ok, string? Note)> SetProxyAsync(DeviceProxyTarget device, string hostIp, int port, CancellationToken ct)
    {
        var r = await Adb(device.Target, ["shell", "settings", "put", "global", "http_proxy", $"{hostIp}:{port}"], ct);
        return r.ExitCode == 0 ? (true, null) : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    public async Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceProxyTarget device, CancellationToken ct)
    {
        // ":0" is the documented "no proxy" sentinel for the global http_proxy setting.
        var r = await Adb(device.Target, ["shell", "settings", "put", "global", "http_proxy", ":0"], ct);
        return r.ExitCode == 0 ? (true, null) : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
