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

    // Default TrustStore path. iOS 16+ (and the palera1n-rootful iPhone 8 here) keeps it under
    // /private/var/protected/trustd/private/; older iOS used /private/var/Keychains/. Overridable via StorePath.
    private const string DefaultStore = "/private/var/protected/trustd/private/TrustStore.sqlite3";

    // {store}/{sha256}/{subj}/{data} placeholders. iOS 16's `tsettings` schema keys on a `sha256` column
    // (legacy iOS used `sha1`). INSERT OR REPLACE the trust row (tset NULL = trusted root), then kill trustd so
    // it reloads on next use. sqlite3 is NOT on a bare jailbreak by default, so install it first (apt, Procursus)
    // - and chain with && so a failure propagates to the ssh exit code (the old `; echo ok` masked failures).
    private const string DefaultCommand =
        "{ command -v sqlite3 >/dev/null 2>&1 || apt-get install -y sqlite3 >/dev/null 2>&1; } && " +
        "sqlite3 {store} \"INSERT OR REPLACE INTO tsettings (sha256, subj, tset, data) " +
        "VALUES (X'{sha256}', X'{subj}', NULL, X'{data}');\" && killall -9 trustd 2>/dev/null; " +
        "sqlite3 {store} \"SELECT 'row-present' FROM tsettings WHERE sha256=X'{sha256}';\"";

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
            .Replace("{sha256}", CaCertPrep.IosCertSha256Hex(cert))
            .Replace("{sha1}", CaCertPrep.IosCertSha1Hex(cert)) // legacy template support
            .Replace("{subj}", CaCertPrep.IosSubjectDerHex(cert))
            .Replace("{data}", CaCertPrep.DerHex(cert));

        var r = await runner.RunAsync("ssh",
            ["-p", ssh.Port, "-i", ssh.KeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{ssh.Host}", cmd], ct);
        // The default command ends by SELECTing the row back, so a real success prints "row-present". This
        // catches the failure the old `; echo ok` masked (e.g. sqlite3 missing, wrong schema/path).
        var verified = r.Stdout.Contains("row-present");
        if (r.ExitCode == 0 && verified) return (true, "trust store updated (row verified)");
        return (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }
}
