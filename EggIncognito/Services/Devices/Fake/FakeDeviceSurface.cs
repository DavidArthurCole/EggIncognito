using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices.Fake;

public sealed class FakeProxyConfigurator(string platform) : IDeviceProxyConfigurator {
    public string Platform => platform;

    public Task<(bool Ok, string? Note)> SetProxyAsync(DeviceTarget device, string hostIp, int port,
        CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, $"fake device accepted proxy {hostIp}:{port}"));

    public Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceTarget device, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, "fake device cleared proxy"));
}

public sealed class FakeCaInstaller(string platform) : IDeviceCaInstaller {
    public string Platform => platform;

    public Task<(bool Ok, string? Note)> InstallAsync(DeviceTarget device, string caPath, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, "fake device trusts the capture CA"));
}
