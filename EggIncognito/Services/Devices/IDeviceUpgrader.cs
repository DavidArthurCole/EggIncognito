using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;

namespace EggIncognito.Services.Devices;

// Seam for the future auto-upgrade pass: on a detected new_version, download + install + extract + record.
// This pass ships NoopDeviceUpgrader (logs and returns); the later pass swaps in a real implementation
// that drives the existing AndroidRunner / IosRunner. The probe service + classification stay unchanged.
public interface IDeviceUpgrader
{
    Task MaybeUpgradeAsync(Device device, DeviceProbeResult result, CancellationToken ct);
}
