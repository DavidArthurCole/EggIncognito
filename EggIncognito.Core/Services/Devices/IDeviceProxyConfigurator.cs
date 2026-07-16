namespace EggIncognito.Core.Services.Devices;


public interface IDeviceProxyConfigurator
{
   
    string Platform { get; }

   
    Task<(bool Ok, string? Note)> SetProxyAsync(DeviceProxyTarget device, string hostIp, int port, CancellationToken ct);

   
    Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceProxyTarget device, CancellationToken ct);
}
public sealed record DeviceProxyTarget(string Id, string Platform, string Target);
