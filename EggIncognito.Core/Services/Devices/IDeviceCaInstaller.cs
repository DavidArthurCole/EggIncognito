namespace EggIncognito.Core.Services.Devices;

public interface IDeviceCaInstaller {
    string Platform { get; }

    Task<(bool Ok, string? Note)> InstallAsync(DeviceTarget device, string caPath, CancellationToken ct);
}
