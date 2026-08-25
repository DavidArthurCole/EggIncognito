
namespace EggIncognito.Core.Services.Devices;

public interface IDeviceConnectionFactory {
    IDeviceConnection? For(DeviceTarget target);
    SshDeviceConnection? Ios(string? hostFallback = null);
}

public sealed class DeviceConnectionFactory(
    IProcessRunner runner,
    DeviceCaptureConfig config,
    DeviceTransportConfig? transportConfig = null,
    IHttpClientFactory? httpFactory = null)
    : IDeviceConnectionFactory {
    public IDeviceConnection? For(DeviceTarget target) {
        string? platform = target.Platform?.ToLowerInvariant();
        if (transportConfig is not null && httpFactory is not null
            && transportConfig.Mode == DeviceTransportMode.Remote
            && platform is Platforms.Android or Platforms.Ios) {
            return new RemoteDeviceConnection(httpFactory.CreateClient(), transportConfig, target);
        }

        return platform switch {
            Platforms.Android => new AdbDeviceConnection(runner, target.Target),
            Platforms.Ios => Ios(target.Target),
            _ => null
        };
    }

    public SshDeviceConnection? Ios(string? hostFallback = null) {
        string? host = config.IosSshHost ?? hostFallback;
        return string.IsNullOrEmpty(host) || string.IsNullOrEmpty(config.IosSshKeyPath)
            ? null
            : new SshDeviceConnection(runner, new SshEndpoint(host, config.IosSshPort, config.IosSshKeyPath));
    }
}
