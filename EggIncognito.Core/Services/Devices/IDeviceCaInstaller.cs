namespace EggIncognito.Core.Services.Devices;

// Installs + trusts the capture root CA on a single device, so the per-device proxy's MITM TLS handshake
// is accepted and auxbrain flows decrypt. Both farm devices are rooted/jailbroken, so this is fully
// automatable over the same control channel the proxy + probes use (adb / ssh), no on-device tap.
// Idempotent: re-installing the same cert is a no-op. Never throws: a failure returns (false, note) so a
// device that cannot be trusted does not break the capture manager.
public interface IDeviceCaInstaller
{
    // The platform this installer handles ("android" / "ios"), matched against Device.Platform.
    string Platform { get; }

    // Install + trust the DER cert at caPath on the device. Returns (ok, note).
    Task<(bool Ok, string? Note)> InstallAsync(DeviceCaTarget device, string caPath, CancellationToken ct);
}

// Minimal device shape the CA installers need, decoupled from the Data-layer Device entity.
// Target = the adb serial (android) or the ssh host (ios), same as the proxy path.
public sealed record DeviceCaTarget(string Id, string Platform, string Target);
