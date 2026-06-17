using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

public sealed class NoopDeviceUpgrader(ILogger<NoopDeviceUpgrader> logger) : IDeviceUpgrader
{
    public Task MaybeUpgradeAsync(Device device, DeviceProbeResult result, CancellationToken ct)
    {
        logger.LogInformation(
            "device upgrade: {Id} new version {Version} detected; auto-upgrade not yet wired",
            device.Id, result.InstalledAppVersion);
        return Task.CompletedTask;
    }
}
