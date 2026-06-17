namespace EggIncognito.Core.Services.Devices;

// A pure status read of one device: is it reachable, what Egg Inc version is installed. No extraction.
// Never throws: shell failure becomes Reachable: false + a human-readable Note.
public interface IDeviceProbe
{
    Task<DeviceProbeResult> ProbeAsync(CancellationToken ct);
}

public sealed record DeviceProbeResult(
    bool Reachable, string? InstalledAppVersion, string? InstalledBuild, string? Note);
