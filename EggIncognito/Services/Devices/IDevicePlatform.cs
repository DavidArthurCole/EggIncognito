using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public interface IDevicePlatform {
    string Platform { get; }
    DeviceCapabilities Capabilities { get; }

    Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind, string name,
        CancellationToken ct);
    Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target, DeviceAssetKind kind,
        CancellationToken ct);

    Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct);
    Task<StoreCheckResult> DriveStoreUpdateAsync(DeviceTarget target, CancellationToken ct,
        Action<string>? progress = null);

    Task<DeviceResult> SetProxyAsync(DeviceTarget target, string hostIp, int port, CancellationToken ct);
    Task<DeviceResult> ClearProxyAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> InstallCaAsync(DeviceTarget target, string caPath, CancellationToken ct);

    Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct);
    Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct);

    Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target, string scriptBody,
        string? addrOffset, CancellationToken ct);
}

public interface IDevicePlatforms {
    IDevicePlatform For(string platform);
}

public sealed class DevicePlatforms : IDevicePlatforms {
    private readonly Dictionary<string, IDevicePlatform> _byPlatform;
    private readonly NullDevicePlatform _fallback = new();

    public DevicePlatforms(IEnumerable<IDevicePlatform> platforms) {
        _byPlatform = platforms.ToDictionary(p => p.Platform, StringComparer.OrdinalIgnoreCase);
    }

    public IDevicePlatform For(string platform) =>
        !string.IsNullOrEmpty(platform) && _byPlatform.TryGetValue(platform, out var p) ? p : _fallback;
}

public sealed class NullDevicePlatform : IDevicePlatform {
    public string Platform => "none";
    public DeviceCapabilities Capabilities => DeviceCapabilities.None;

    private static string Note(DeviceTarget t) => $"no handler for platform '{t.Platform}'";

    public Task<DeviceResult<byte[]>> PullAppBinaryAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult<byte[]>.Unsupported(Note(target)));

    public Task<DeviceResult<byte[]>> ReadAssetAsync(DeviceTarget target, DeviceAssetKind kind, string name,
        CancellationToken ct) => Task.FromResult(DeviceResult<byte[]>.Unsupported(Note(target)));

    public Task<DeviceResult<IReadOnlyList<string>>> ListAssetsAsync(DeviceTarget target, DeviceAssetKind kind,
        CancellationToken ct) => Task.FromResult(DeviceResult<IReadOnlyList<string>>.Unsupported(Note(target)));

    public Task<DeviceProbeResult> ProbeAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(new DeviceProbeResult(false, null, null, Note(target)));

    public Task<StoreCheckResult> DriveStoreUpdateAsync(DeviceTarget target, CancellationToken ct,
        Action<string>? progress = null) =>
        Task.FromResult(new StoreCheckResult(false, null, null, false, false, "unsupported", Note(target)));

    public Task<DeviceResult> SetProxyAsync(DeviceTarget target, string hostIp, int port, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> ClearProxyAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> InstallCaAsync(DeviceTarget target, string caPath, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> RestartAppAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> LockAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> UnlockAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult> KillAppAsync(DeviceTarget target, CancellationToken ct) =>
        Task.FromResult(DeviceResult.Unsupported(Note(target)));

    public Task<DeviceResult<ParticleCaptureModel.Model>> CaptureParticlesAsync(DeviceTarget target, string scriptBody,
        string? addrOffset, CancellationToken ct) =>
        Task.FromResult(DeviceResult<ParticleCaptureModel.Model>.Unsupported(Note(target)));
}
