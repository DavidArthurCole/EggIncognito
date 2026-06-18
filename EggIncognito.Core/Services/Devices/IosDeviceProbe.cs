namespace EggIncognito.Core.Services.Devices;

// iOS status probe via libimobiledevice: `ideviceinstaller -u <udid> -l -o xml`, parse the matching
// app's CFBundleShortVersionString (version, e.g. 1.36) + CFBundleVersion (build, e.g. 1.36.0.2). Non-zero
// exit (tool missing / device not paired) => not reachable. App absent => reachable, no version.
public sealed class IosDeviceProbe(IProcessRunner runner, string udid, string bundleId) : IDeviceProbe
{
    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct)
    {
        var r = await runner.RunAsync("ideviceinstaller", ["-u", udid, "-l", "-o", "xml"], ct);
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        var (app, build) = DeviceParsing.IosVersion(r.Stdout, bundleId);
        return app is null
            ? new DeviceProbeResult(true, null, null, $"{bundleId} not installed")
            : new DeviceProbeResult(true, app, build, null);
    }
}
