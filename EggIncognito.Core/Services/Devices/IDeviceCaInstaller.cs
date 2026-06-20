namespace EggIncognito.Core.Services.Devices;

// Installs + trusts the capture root CA on a single device, so the per-device proxy's MITM TLS handshake
// is accepted and auxbrain flows decrypt (otherwise the proxy sees auxbrain CONNECTs but never a decrypted
// request, and no rinfo is harvested). Both farm devices are rooted/jailbroken, so this is fully automatable
// over the same control channel the proxy + probes use (adb / ssh) with no on-device tap.
//
// Per platform: Android writes the cert into the system trust store (rooted, via a tmpfs bind-mount over the
// read-only conscrypt cacerts dir on Android 14); iOS inserts a trust record into the on-device
// TrustStore.sqlite3 over ssh. The DER cert is exported by the proxy at caPath; the installer reads it.
//
// Idempotent: re-installing the same cert is a no-op (Android overwrites the same hash file; iOS upserts the
// same sha1 row). Never throws: a failure returns (false, note) so a device that cannot be trusted does not
// break the capture manager. Called once on capture start and again whenever the proxy mints a FRESH CA.
public interface IDeviceCaInstaller
{
    // The platform this installer handles ("android" / "ios"), matched against Device.Platform.
    string Platform { get; }

    // Install + trust the DER cert at caPath on the device. Returns (ok, note).
    Task<(bool Ok, string? Note)> InstallAsync(DeviceCaTarget device, string caPath, CancellationToken ct);
}

// Minimal device shape the CA installers need, decoupled from the Data-layer Device entity (Core has no
// dependency on Data). Target = the adb serial (android) or the ssh host (ios), same as the proxy path.
public sealed record DeviceCaTarget(string Id, string Platform, string Target);
