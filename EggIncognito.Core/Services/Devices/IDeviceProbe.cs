namespace EggIncognito.Core.Services.Devices;

public interface IDeviceProbe {
    Task<DeviceProbeResult> ProbeAsync(CancellationToken ct);
}

public sealed record DeviceProbeResult(
    bool Reachable,
    string? InstalledAppVersion,
    string? InstalledBuild,
    string? Note);
