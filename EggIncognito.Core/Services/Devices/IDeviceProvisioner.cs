namespace EggIncognito.Core.Services.Devices;

public static class ProvisionStates {
    public const string Creating = "creating";
    public const string Booting = "booting";
    public const string Ready = "ready";
    public const string Stopped = "stopped";
    public const string Failed = "failed";
    public const string Destroyed = "destroyed";

    public static bool IsLive(string? state) =>
        state is Creating or Booting or Ready;
}

public sealed record ProvisionSpec(string Kind, string Image, string? Label = null, int? AdbPort = null);

public sealed record ProvisionedInstance(
    string InstanceId,
    string Kind,
    string Image,
    string State,
    string? AdbSerial = null,
    string? HostRef = null,
    DateTimeOffset CreatedAt = default,
    string? Note = null);

[Flags]
public enum ProvisionerCapabilities {
    None = 0,
    Create = 1 << 0,
    StartStop = 1 << 1,
    Destroy = 1 << 2,
    List = 1 << 3
}

public interface IDeviceProvisioner {
    string Kind { get; }
    ProvisionerCapabilities Capabilities { get; }
    Task<DeviceResult<ProvisionedInstance>> CreateAsync(ProvisionSpec spec, CancellationToken ct);
    Task<DeviceResult> StartAsync(string instanceId, CancellationToken ct);
    Task<DeviceResult> StopAsync(string instanceId, CancellationToken ct);
    Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct);
    Task<DeviceResult<IReadOnlyList<ProvisionedInstance>>> ListAsync(CancellationToken ct);
}

public interface IDeviceProvisioners {
    IReadOnlyList<string> Kinds { get; }
    IDeviceProvisioner For(string kind);
}

public sealed class DeviceProvisioners : IDeviceProvisioners {
    private readonly Dictionary<string, IDeviceProvisioner> _byKind;
    private readonly NullDeviceProvisioner _fallback = new();

    public DeviceProvisioners(IEnumerable<IDeviceProvisioner> provisioners) {
        _byKind = provisioners.ToDictionary(p => p.Kind, StringComparer.OrdinalIgnoreCase);
        Kinds = [.. _byKind.Keys.OrderBy(k => k, StringComparer.Ordinal)];
    }

    public IReadOnlyList<string> Kinds { get; }

    public IDeviceProvisioner For(string kind) =>
        !string.IsNullOrEmpty(kind) && _byKind.TryGetValue(kind, out var p) ? p : _fallback;
}

public sealed class NullDeviceProvisioner : IDeviceProvisioner {
    public string Kind => "none";
    public ProvisionerCapabilities Capabilities => ProvisionerCapabilities.None;

    private static string Note(string kind) => $"no provisioner for kind '{kind}'";

    public Task<DeviceResult<ProvisionedInstance>> CreateAsync(ProvisionSpec spec, CancellationToken ct) =>
        Task.FromResult(DeviceResult<ProvisionedInstance>.Unsupported(Note(spec.Kind)));

    public Task<DeviceResult> StartAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(Kind)));

    public Task<DeviceResult> StopAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(Kind)));

    public Task<DeviceResult> DestroyAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(Kind)));

    public Task<DeviceResult<IReadOnlyList<ProvisionedInstance>>> ListAsync(CancellationToken ct) =>
        Task.FromResult(DeviceResult<IReadOnlyList<ProvisionedInstance>>.Success([]));
}
