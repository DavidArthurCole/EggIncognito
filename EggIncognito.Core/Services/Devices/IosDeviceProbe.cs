namespace EggIncognito.Core.Services.Devices;

public sealed class IosDeviceProbe(IProcessRunner runner, string udid, string bundleId) : IDeviceProbe {
    public async Task<DeviceProbeResult> ProbeAsync(CancellationToken ct) {
        var r = await runner.RunAsync("ideviceinstaller", ["-u", udid, "-l", "-o", "xml"], ct);
        if (r.ExitCode != 0)
            return new DeviceProbeResult(false, null, null, DeviceParsing.TrimNote(r.Stderr + r.Stdout));

        (string? app, string? build) = DeviceParsing.IosVersion(r.Stdout, bundleId);
        return app is null
            ? new DeviceProbeResult(true, null, null, $"{bundleId} not installed")
            : new DeviceProbeResult(true, app, build, null);
    }
}
