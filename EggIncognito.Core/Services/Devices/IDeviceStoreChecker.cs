namespace EggIncognito.Core.Services.Devices;

// Tells a plugged-in device to ask ITS OWN store (Play on Android, App Store on iOS) whether the Egg Inc
// app has an update, and to install it if so: drives the on-device store over its control channel (adb /
// ssh tweak trigger), then re-reads the installed version to see whether it climbed.
// Returns a verdict the UI can show directly. Never throws: a control-channel failure is reported as
// Reachable=false with a note.
public interface IDeviceStoreChecker
{
    string Platform { get; } // "android" / "ios", matched against Device.Platform

    // progress: optional per-poll-round callback so a long check (~6 min) is observable while it runs.
    // Null = no reporting (e.g. tests that ignore progress).
    Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceStoreTarget device, CancellationToken ct, Action<string>? progress = null);
}

// What the checker needs about a device, decoupled from the Data-layer Device entity.
public sealed record DeviceStoreTarget(string Id, string Platform, string Target, string Package);

// Outcome of a device-driven store check.
// Reachable: the device answered. InstalledBefore/After: version read before/after driving the store.
// UpdateFound: the store reported/applied a newer version. Installed: the update actually landed
// (verified by re-read). Action: machine-readable for the UI ("up_to_date" | "updated" | "updating" |
// "unreachable" | "error"). Note: human detail.
public sealed record StoreCheckResult(
    bool Reachable, string? InstalledBefore, string? InstalledAfter,
    bool UpdateFound, bool Installed, string Action, string? Note);
