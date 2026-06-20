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

    // Default rooted Android-14 system-CA install, NAMESPACE-CORRECT. On Android 14 the system cacerts dirs
    // are read-only, AND a plain `mount` in the adb/su shell lands in that shell's mount namespace - apps fork
    // from zygote, which shares INIT's (PID 1) namespace, so a shell-local mount is invisible to the game.
    // Fix: build a tmpfs dir holding the existing system certs PLUS ours, then bind-mount it over both cacerts
    // paths INSIDE init's namespace via `nsenter -t 1 -m`, so zygote + every app launched after (the force-
    // restart that follows) inherits the mount and the game's TrustManager sees our CA. Falls back to a plain
    // bind when nsenter is unavailable (better than nothing). The conscrypt APEX path is what Android 14 reads.
    // Namespace-correct Android-14 system-CA install that REPORTS what actually happened (every step echoes
    // a `diag:` line, surfaced in the install note) instead of swallowing errors behind `|| true`. The mount
    // must land in INIT's (PID 1) mount namespace so zygote + every app forked after inherits it - a mount in
    // the adb/su shell's own namespace is invisible to the game. We try `nsenter -t 1 -m` (preferred), report
    // whether nsenter exists + whether the bind took, then VERIFY by reading our hash file back through the
    // init namespace. The caller force-restarts the app afterwards so it forks with the mount visible.
    private const string DefaultScript =
        "D=/data/local/tmp/eggcacerts; rm -rf $D; mkdir -p $D; " +
        "cp /system/etc/security/cacerts/* $D/ 2>/dev/null; " +
        "cp /apex/com.android.conscrypt/cacerts/* $D/ 2>/dev/null; " +
        "cp {pem_path} $D/{hash}.0 && chmod 644 $D/{hash}.0 && chown 0:0 $D/{hash}.0; " +
        "chcon u:object_r:system_security_cacerts_file:s0 $D/{hash}.0 2>&1 | sed 's/^/diag chcon: /'; " +
        "if command -v nsenter >/dev/null 2>&1; then echo 'diag nsenter: present'; NS='nsenter -t 1 -m --'; " +
        "  else echo 'diag nsenter: MISSING (toybox lacks it - falling back to shell-ns mount, game will NOT see it)'; NS=''; fi; " +
        "for T in /system/etc/security/cacerts /apex/com.android.conscrypt/cacerts; do " +
        "  [ -d $T ] || continue; " +
        "  $NS mount --bind $D $T 2>&1 | sed \"s|^|diag mount $T: |\"; " +
        "  echo \"diag mount $T rc=$?\"; " +
        "done; " +
        // Verify from init's namespace: can a freshly-entered ns read our cert at the conscrypt path?
        "if [ -n \"$NS\" ]; then " +
        "  $NS sh -c '[ -f /apex/com.android.conscrypt/cacerts/{hash}.0 ] && echo present || echo absent' 2>&1 | sed 's/^/diag verify-initns: /'; " +
        "fi; " +
        "echo \"diag done {hash}.0\"";

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
        // `su -c`, so the template can be overridden. The script is passed as a single argument. The script
        // echoes `diag ...` lines describing what actually happened (nsenter present? mount rc? cert visible
        // from init's namespace?); those are surfaced in the note + logged so a failed mount is diagnosable
        // instead of always reporting success.
        var r = await Adb(device.Target, ["shell", "su", "0", "sh", "-c", script], ct);
        var diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
        if (r.ExitCode != 0) return (false, $"install script rc={r.ExitCode}: {diag}");
        // The verify line tells the truth: "verify-initns: present" => the game's namespace will see the CA.
        var trusted = r.Stdout.Contains("verify-initns: present");
        return (true, $"{hash}.0 ({(trusted ? "VISIBLE in init-ns" : "NOT verified - see diag")}): {diag}");
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
