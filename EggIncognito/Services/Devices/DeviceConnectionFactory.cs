using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public interface IDeviceConnectionFactory {
    IDeviceConnection? For(string platform, string target);
    SshDeviceConnection? Ios(string? hostFallback = null);
}

public sealed class DeviceConnectionFactory(IProcessRunner runner, DeviceCaptureConfig config) : IDeviceConnectionFactory {
    public IDeviceConnection? For(string platform, string target) =>
        platform?.ToLowerInvariant() switch {
            "android" => new AdbDeviceConnection(runner, target),
            "ios" => Ios(target),
            _ => null
        };

    public SshDeviceConnection? Ios(string? hostFallback = null) {
        var host = config.IosSshHost ?? hostFallback;
        return string.IsNullOrEmpty(host) || string.IsNullOrEmpty(config.IosSshKeyPath)
            ? null
            : new SshDeviceConnection(runner, new SshEndpoint(host, config.IosSshPort, config.IosSshKeyPath));
    }
}
