using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

// Drives a device from its installed game version toward targetAppVersion. Never throws: failures are
// reported in the outcome so the poll loop continues. Idempotent: a no-op when already current. One impl
// per platform (android = download + adb install; ios = ssh + eggupdate.dylib tweak trigger).
public interface IDeviceUpdater
{
    Task<DeviceUpdateOutcome> UpdateAsync(Device device, string targetAppVersion, CancellationToken ct);
}

// Started = the update was actually attempted (download + install kicked off). Verified = a re-probe
// confirmed the installed version reached the target. Note carries the human-readable detail for logs/UI.
public sealed record DeviceUpdateOutcome(
    bool Started, bool Verified, string? FromVersion, string? ToVersion, string Note);
