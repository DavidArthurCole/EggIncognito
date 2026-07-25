using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

public enum DeviceAssetKind {
    Mesh,
    Texture
}

public interface IDeviceAssetReader {
    string Platform { get; }
    Task<byte[]?> ReadAsync(Device device, DeviceAssetKind kind, string name, CancellationToken ct);
    Task<IReadOnlyList<string>> ListAsync(Device device, DeviceAssetKind kind, CancellationToken ct);
}
