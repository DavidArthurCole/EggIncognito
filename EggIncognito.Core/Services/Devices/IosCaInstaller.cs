using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

public sealed class IosCaInstaller(IProcessRunner runner, IosCaInstaller.SshConfig ssh) : IDeviceCaInstaller {
    private const string DefaultStore = "/private/var/protected/trustd/private/TrustStore.sqlite3";


    private const string DefaultCommand =
        "{ command -v sqlite3 >/dev/null 2>&1 || apt-get install -y sqlite3 >/dev/null 2>&1; } && " +
        "sqlite3 {store} \"INSERT OR REPLACE INTO tsettings (sha256, subj, tset, data) " +
        "VALUES (X'{sha256}', X'{subj}', NULL, X'{data}');\" && killall -9 trustd 2>/dev/null; " +
        "sqlite3 {store} \"SELECT 'row-present' FROM tsettings WHERE sha256=X'{sha256}';\"";

    public string Platform => "ios";

    public async Task<(bool Ok, string? Note)>
        InstallAsync(DeviceTarget device, string caPath, CancellationToken ct) {
        if (string.IsNullOrEmpty(ssh.Host) || string.IsNullOrEmpty(ssh.KeyPath))
            return (false, "ios ssh host/key not configured");
        if (!File.Exists(caPath)) return (false, $"ca file not found: {caPath}");

        X509Certificate2 cert;
        try {
            cert = X509CertificateLoader.LoadCertificateFromFile(caPath);
        } catch (Exception ex) {
            return (false, $"could not read ca: {ex.Message}");
        }

        string cmd = (ssh.CommandTemplate ?? DefaultCommand)
            .Replace("{store}", ssh.StorePath ?? DefaultStore)
            .Replace("{sha256}", CaCertPrep.IosCertSha256Hex(cert))
            .Replace("{sha1}", CaCertPrep.IosCertSha1Hex(cert))
            .Replace("{subj}", CaCertPrep.IosSubjectDerHex(cert))
            .Replace("{data}", CaCertPrep.DerHex(cert));

        var r = await runner.RunAsync("ssh", new SshEndpoint(ssh.Host!, ssh.Port, ssh.KeyPath!).SshArgs(cmd), ct);

        bool verified = r.Stdout.Contains("row-present");
        if (r.ExitCode == 0 && verified) return (true, "trust store updated (row verified)");
        return (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    public sealed record SshConfig(
        string? Host,
        string Port,
        string? KeyPath,
        string? CommandTemplate,
        string? StorePath);
}
