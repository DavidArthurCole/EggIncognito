namespace EggIncognito.Core.Services.Devices;

// Tells a plugged-in device to ask ITS OWN store whether the Egg Inc app has an update, and to install it if
// so. The device's store (Play on Android, App Store on iOS) is the source of truth, NOT a server-side
// version list: we drive the on-device store over the device's control channel (adb / ssh tweak trigger),
// then re-read the installed version to see whether it climbed.
//
// Returns a verdict the UI can show directly: was an update found, did it install, and the before/after
// versions. Never throws: a control-channel failure is reported as Reachable=false with a note.
public interface IDeviceStoreChecker
{
    string Platform { get; } // "android" / "ios", matched against Device.Platform

    Task<StoreCheckResult> CheckAndUpdateAsync(DeviceStoreTarget device, CancellationToken ct);
}

// What the checker needs about a device, decoupled from the Data-layer Device entity.
public sealed record DeviceStoreTarget(string Id, string Platform, string Target, string Package);

// Outcome of a device-driven store check.
//   Reachable      : the device answered (we could read its installed version).
//   InstalledBefore: version read before driving the store.
//   InstalledAfter : version read after (== before when nothing changed).
//   UpdateFound    : the store reported / applied a newer version (after > before, or the store signalled one).
//   Installed      : the update actually landed (after > before, verified by re-read).
//   Action         : machine-readable for the UI ("up_to_date" | "updated" | "updating" | "unreachable" | "error").
//   Note           : human detail.
public sealed record StoreCheckResult(
    bool Reachable, string? InstalledBefore, string? InstalledAfter,
    bool UpdateFound, bool Installed, string Action, string? Note);
