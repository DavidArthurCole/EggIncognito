namespace EggIncognito.Core.Services.Devices;

public interface IDeviceProxyConfigurator {
    string Platform { get; }

    Task<(bool Ok, string? Note)>
        SetProxyAsync(DeviceTarget device, string hostIp, int port, CancellationToken ct);

    Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceTarget device, CancellationToken ct);
}
