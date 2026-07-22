namespace EggIncognito.Core.Services.Devices;


public interface IDeviceCaInstaller {

    string Platform { get; }


    Task<(bool Ok, string? Note)> InstallAsync(DeviceCaTarget device, string caPath, CancellationToken ct);
}

public sealed record DeviceCaTarget(string Id, string Platform, string Target);
