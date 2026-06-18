namespace EggIncognito.Core.Services.Devices;

// Points a single device's system HTTP proxy at a capture listener (or clears it), so the device's
// auxbrain traffic flows through the persistent per-device capture and its rinfo (build/clientVersion)
// can be harvested off the wire. Idempotent: re-running with the same target is a no-op on the device.
//
// Per platform: Android via adb settings, iOS via ssh writing the on-device network prefs. The capture
// host address + port are passed in (caller derives them); this type only knows how to apply them to a
// device through its control channel. Never throws: a push failure returns false with the reason, so a
// device that cannot be reconfigured does not break the capture manager or the probe loop.
public interface IDeviceProxyConfigurator
{
    // The platform this configurator handles ("android" / "ios"), matched against Device.Platform.
    string Platform { get; }

    // Set the device's HTTP proxy to hostIp:port. Returns (ok, note).
    Task<(bool Ok, string? Note)> SetProxyAsync(DeviceProxyTarget device, string hostIp, int port, CancellationToken ct);

    // Clear the device's HTTP proxy (teardown). Returns (ok, note).
    Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceProxyTarget device, CancellationToken ct);
}

// Minimal device shape the configurators need, decoupled from the Data-layer Device entity so Core has no
// dependency on Data. The caller maps its Device row onto this.
public sealed record DeviceProxyTarget(string Id, string Platform, string Target);
