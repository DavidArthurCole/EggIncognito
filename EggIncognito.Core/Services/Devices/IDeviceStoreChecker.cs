namespace EggIncognito.Core.Services.Devices;


public interface IDeviceStoreChecker {
    string Platform { get; }



    Task<StoreCheckResult> CheckAndUpdateAsync(
        DeviceStoreTarget device, CancellationToken ct, Action<string>? progress = null);
}
public sealed record DeviceStoreTarget(string Id, string Platform, string Target, string Package);


public sealed record StoreCheckResult(
    bool Reachable, string? InstalledBefore, string? InstalledAfter,
    bool UpdateFound, bool Installed, string Action, string? Note);
