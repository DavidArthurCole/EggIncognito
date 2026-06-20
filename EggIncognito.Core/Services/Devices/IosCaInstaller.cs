using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

// Installs the capture CA into a JAILBROKEN iOS device's system trust store over ssh, so the proxy's MITM
// TLS is accepted and auxbrain flows decrypt. iOS keeps trusted roots in TrustStore.sqlite3 (`tsettings`
// table: sha1 = SHA-1 of the cert DER [primary key], subj = DER subject, tset = trust-settings plist [NULL
// => trust as a root], data = full DER cert). We INSERT OR REPLACE one row with all blobs baked in as hex
// literals (no file push needed), then restart trustd so the new root takes effect.
//
// The sqlite path + the install/reload commands are config-templated (DeviceCapture:Ios:CaInstallCommand)
// with a working jailbroken default. ssh creds reuse the proxy/updater SshConfig. device.Target is the UDID
// (unused for ssh; the ssh host is the LAN address). Idempotent (REPLACE on the same sha1). Never throws.
public sealed class IosCaInstaller(IProcessRunner runner, IosCaInstaller.SshConfig ssh) : IDeviceCaInstaller
{
    public sealed record SshConfig(string? Host, string Port, string? KeyPath, string? CommandTemplate, string? StorePath);

    public string Platform => "ios";

    // Default jailbroken TrustStore path. Newer rootless jailbreaks relocate it (/private/var/protected/...),
    // so it is overridable via StorePath; the iPhone 8 (legacy) uses this path.
    private const string DefaultStore = "/private/var/Keychains/TrustStore.sqlite3";

    // {store}/{sha1}/{subj}/{data} placeholders. INSERT OR REPLACE the trust row (tset NULL = trusted root),
    // then kill trustd so it reloads the store on next use. sqlite3 must exist on the device (most jailbreaks
    // ship it; install via the override if not).
    private const string DefaultCommand =
        "sqlite3 {store} \"INSERT OR REPLACE INTO tsettings (sha1, subj, tset, data) " +
        "VALUES (X'{sha1}', X'{subj}', NULL, X'{data}');\" && killall -9 trustd 2>/dev/null; echo ok";

    public async Task<(bool Ok, string? Note)> InstallAsync(DeviceCaTarget device, string caPath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ssh.Host) || string.IsNullOrEmpty(ssh.KeyPath))
            return (false, "ios ssh host/key not configured");
        if (!File.Exists(caPath)) return (false, $"ca file not found: {caPath}");

        X509Certificate2 cert;
        try { cert = X509CertificateLoader.LoadCertificateFromFile(caPath); }
        catch (Exception ex) { return (false, $"could not read ca: {ex.Message}"); }

        var cmd = (ssh.CommandTemplate ?? DefaultCommand)
            .Replace("{store}", ssh.StorePath ?? DefaultStore)
            .Replace("{sha1}", CaCertPrep.IosCertSha1Hex(cert))
            .Replace("{subj}", CaCertPrep.IosSubjectDerHex(cert))
            .Replace("{data}", CaCertPrep.DerHex(cert));

        var r = await runner.RunAsync("ssh",
            ["-p", ssh.Port, "-i", ssh.KeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{ssh.Host}", cmd], ct);
        return r.ExitCode == 0 ? (true, "trust store updated") : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }
}
