using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

// Installs the capture CA into a ROOTED Android device's SYSTEM trust store over adb, so apps that only
// trust system CAs (and ignore the user store) accept the proxy. On Android 14 (the farm A15) the system
// cacerts dirs are read-only (a /system copy + the conscrypt APEX copy), so the rooted technique is: build
// a tmpfs holding the existing system certs PLUS ours, then bind-mount that tmpfs over both cacerts dirs.
// The cert filename is `<subject_hash_old>.0` (OpenSSL legacy hash), computed in-process by CaCertPrep.
//
// The mount script is config-templated (DeviceCapture:Android:CaInstallScript) with {hash}/{pem_path}
// placeholders so it is tunable per-ROM without a rebuild; a working Android-14 default is built in. The
// PEM is pushed to /data/local/tmp first. adb reaches the device via the host's adb server (same as probes).
// Idempotent (overwrites the same hash file). Never throws: a non-zero adb exit returns (false, note).
public sealed class AdbCaInstaller(IProcessRunner runner, string? installScriptTemplate = null) : IDeviceCaInstaller
{
    public string Platform => "android";

    // Pushed PEM location on the device; referenced by {pem_path} in the script.
    private const string RemotePem = "/data/local/tmp/eggincognito-ca.pem";

    // Default rooted Android-14 system-CA install. Runs in one `su -c` so the mount persists in the global
    // namespace. Steps: ensure the user-cert hash file exists in a writable tmp copy of the existing system
    // store, add ours, then bind-mount the tmp copy over BOTH the legacy /system path and the conscrypt APEX
    // path (Android 14 reads the APEX copy). Restart is left to the proxy-push / app-launch that follows.
    private const string DefaultScript =
        "set -e; " +
        "D=/data/local/tmp/eggcacerts; rm -rf $D; mkdir -p $D; " +
        "cp /system/etc/security/cacerts/* $D/ 2>/dev/null || true; " +
        "cp /apex/com.android.conscrypt/cacerts/* $D/ 2>/dev/null || true; " +
        "cp {pem_path} $D/{hash}.0; chmod 644 $D/{hash}.0; chown root:root $D/{hash}.0; " +
        "mount -t tmpfs tmpfs /system/etc/security/cacerts 2>/dev/null || true; " +
        "cp $D/* /system/etc/security/cacerts/ 2>/dev/null || true; " +
        "chmod 644 /system/etc/security/cacerts/* 2>/dev/null || true; " +
        "if [ -d /apex/com.android.conscrypt/cacerts ]; then " +
        "  mount -t tmpfs tmpfs /apex/com.android.conscrypt/cacerts 2>/dev/null || true; " +
        "  cp $D/* /apex/com.android.conscrypt/cacerts/ 2>/dev/null || true; " +
        "  chmod 644 /apex/com.android.conscrypt/cacerts/* 2>/dev/null || true; " +
        "fi; " +
        "echo installed {hash}.0";

    public async Task<(bool Ok, string? Note)> InstallAsync(DeviceCaTarget device, string caPath, CancellationToken ct)
    {
        if (!File.Exists(caPath)) return (false, $"ca file not found: {caPath}");

        X509Certificate2 cert;
        try { cert = X509CertificateLoader.LoadCertificateFromFile(caPath); }
        catch (Exception ex) { return (false, $"could not read ca: {ex.Message}"); }

        var hash = CaCertPrep.AndroidSubjectHashOld(cert);
        var pem = CaCertPrep.ToPem(cert);

        // Push the PEM by writing it on the device through a here-doc-free `echo` would mangle newlines, so
        // push the local file directly. Write the local PEM to a temp file adb can push.
        var tmp = Path.Combine(Path.GetTempPath(), $"eggincognito-ca-{device.Id}.pem");
        try { await File.WriteAllTextAsync(tmp, pem, ct); }
        catch (Exception ex) { return (false, $"could not stage pem: {ex.Message}"); }

        var push = await Adb(device.Target, ["push", tmp, RemotePem], ct);
        try { File.Delete(tmp); } catch { /* best-effort */ }
        if (push.ExitCode != 0) return (false, "push pem failed: " + DeviceParsing.TrimNote(push.Stderr + push.Stdout));

        var script = (installScriptTemplate ?? DefaultScript)
            .Replace("{hash}", hash)
            .Replace("{pem_path}", RemotePem);

        // Run as root in one shell. `su 0 sh -c '<script>'` is the rooted invocation; some ROMs want
        // `su -c`, so the template can be overridden. The script is passed as a single argument.
        var r = await Adb(device.Target, ["shell", "su", "0", "sh", "-c", script], ct);
        if (r.ExitCode != 0) return (false, "install script failed: " + DeviceParsing.TrimNote(r.Stderr + r.Stdout));
        return (true, $"system CA installed ({hash}.0)");
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
