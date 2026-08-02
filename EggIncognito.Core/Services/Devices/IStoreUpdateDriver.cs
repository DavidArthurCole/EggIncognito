namespace EggIncognito.Core.Services.Devices;

public enum StoreAvailability { Unknown, UpToDate, UpdateOffered, ManualNeeded }

public sealed record StoreProbeOutcome(StoreAvailability Availability, string? StoreVersion, string? Note);

public sealed record TriggerOutcome(bool Ok, string? Note);

public interface IStoreUpdateDriver {
    string Platform { get; }
    string StoreName { get; }
    Task<string?> ReadInstalledAsync(DeviceTarget target, CancellationToken ct);
    Task PrepareAsync(DeviceTarget target, CancellationToken ct);
    Task<StoreProbeOutcome> ProbeStoreAsync(DeviceTarget target, string installed, Action<string>? progress, CancellationToken ct);
    Task<TriggerOutcome> TriggerInstallAsync(DeviceTarget target, Action<string>? progress, CancellationToken ct);
    Task CleanupAsync(DeviceTarget target, CancellationToken ct);
}
