namespace EggIncognito.Core.Services.Devices;

// iOS status probe via libimobiledevice: `ideviceinstaller -u <udid> list`, parse the matching app's
// version from the CSV output. iOS yields no auxbrain build number, so Build is always null. Non-zero
// exit (tool missing / device not paired) => not reachable. App absent => reachable, no version.
public sealed class IosDeviceProbe(IProcessRunner runner, string udid, string bundleId) : IDeviceProbe
{
    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync("ideviceinstaller", ["-u", udid, "list"], ct);
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        var app = DeviceParsing.IosAppVersion(r.Stdout, bundleId);
        return app is null
            ? new DeviceProbeResult(true, null, null, $"{bundleId} not installed")
            : new DeviceProbeResult(true, app, null, null);
    }
}
