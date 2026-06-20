using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

// Installs the capture CA into a ROOTED Android device's SYSTEM trust store over adb, so apps that only
// trust system CAs (Egg Inc ignores the user store) accept the proxy and auxbrain flows decrypt.
//
// Android 14 (the farm A15) makes this hard:
//   - the system cacerts dirs are read-only (a /system copy + the conscrypt APEX copy);
//   - apps fork from zygote, which has its OWN mount namespace, so a bind-mount done in the adb/su shell is
//     invisible to the game; the mount must be visible to zygote's namespace.
//   - entering PID 1's mount ns (`nsenter -t 1 -m`) is SELinux-DENIED for the su domain on this ROM
//     ("/proc/1/ns/mnt: Permission denied"), so the PID-1 trick is out.
// Working approach: build a tmpfs dir of the existing system certs PLUS ours, bind-mount it over both cacerts
// paths, and enter ZYGOTE's mount namespace (not PID 1) to do it - zygote is the parent of every app, and its
// ns is reachable by the su domain where PID 1 is not. App processes already running don't see it, so the
// caller force-restarts Egg Inc afterwards (a fresh fork inherits zygote's mount).
//
// The whole script is PUSHED AS A FILE and executed, never passed as `sh -c "..."` (adb word-splits a single
// arg, so only the first ;-segment ran - that bug wrote the cert to "/<hash>.0" with $D empty). Every step
// echoes a `diag` line, surfaced in the install note. Config override: DeviceCapture:Android:CaInstallScript.
public sealed class AdbCaInstaller(IProcessRunner runner, string? installScriptTemplate = null) : IDeviceCaInstaller
{
    public string Platform => "android";

    private const string RemotePem = "/data/local/tmp/eggincognito-ca.pem";
    private const string RemoteScript = "/data/local/tmp/eggincognito-ca-install.sh";

    // Pushed + executed as a file (so $vars and multi-line logic survive). Mounts into zygote's mount ns,
    // since PID 1's is SELinux-denied. Reports every step. {hash}/{pem_path} substituted before push.
    private const string DefaultScript =
        "#!/system/bin/sh\n" +
        "D=/data/local/tmp/eggcacerts\n" +
        "rm -rf \"$D\"; mkdir -p \"$D\"\n" +
        "cp /system/etc/security/cacerts/* \"$D/\" 2>/dev/null\n" +
        "cp /apex/com.android.conscrypt/cacerts/* \"$D/\" 2>/dev/null\n" +
        "cp {pem_path} \"$D/{hash}.0\" && chmod 644 \"$D/{hash}.0\" && chown 0:0 \"$D/{hash}.0\"\n" +
        "chcon u:object_r:system_security_cacerts_file:s0 \"$D\"/* 2>&1 | sed 's/^/diag chcon: /'\n" +
        "[ -f \"$D/{hash}.0\" ] && echo 'diag staged: ok' || echo 'diag staged: FAILED (cert not in $D)'\n" +
        // Find zygote (prefer 64-bit). Its mount namespace is what apps inherit and is reachable where PID 1 is not.
        "ZP=$(pidof zygote64 2>/dev/null || pidof zygote 2>/dev/null); echo \"diag zygote-pid: ${ZP:-none}\"\n" +
        "command -v nsenter >/dev/null 2>&1 && echo 'diag nsenter: present' || echo 'diag nsenter: MISSING'\n" +
        "for T in /system/etc/security/cacerts /apex/com.android.conscrypt/cacerts; do\n" +
        "  [ -d \"$T\" ] || continue\n" +
        "  if [ -n \"$ZP\" ] && command -v nsenter >/dev/null 2>&1; then\n" +
        "    nsenter -t \"$ZP\" -m -- mount --bind \"$D\" \"$T\" 2>&1 | sed \"s|^|diag zygote-mount $T: |\"\n" +
        "    echo \"diag zygote-mount $T rc=$?\"\n" +
        "  fi\n" +
        // Also bind in the current ns (covers tooling that reads from this context + some ROMs propagate).
        "  mount --bind \"$D\" \"$T\" 2>&1 | sed \"s|^|diag shell-mount $T: |\"\n" +
        "done\n" +
        // Verify from zygote's namespace: will a freshly-forked app see our cert?
        "if [ -n \"$ZP\" ] && command -v nsenter >/dev/null 2>&1; then\n" +
        "  nsenter -t \"$ZP\" -m -- sh -c '[ -f /apex/com.android.conscrypt/cacerts/{hash}.0 ] && echo present || echo absent' 2>&1 | sed 's/^/diag verify-zygotens: /'\n" +
        "fi\n" +
        "echo 'diag done {hash}.0'\n";

    public async Task<(bool Ok, string? Note)> InstallAsync(DeviceCaTarget device, string caPath, CancellationToken ct)
    {
        if (!File.Exists(caPath)) return (false, $"ca file not found: {caPath}");

        X509Certificate2 cert;
        try { cert = X509CertificateLoader.LoadCertificateFromFile(caPath); }
        catch (Exception ex) { return (false, $"could not read ca: {ex.Message}"); }

        var hash = CaCertPrep.AndroidSubjectHashOld(cert);
        var pem = CaCertPrep.ToPem(cert);

        // Stage PEM + script as local temp files and push both (a pushed script preserves $vars + newlines,
        // which `adb shell su 0 sh -c "<script>"` does NOT - adb word-splits the one arg).
        var tmpPem = Path.Combine(Path.GetTempPath(), $"eggincognito-ca-{device.Id}.pem");
        var tmpScript = Path.Combine(Path.GetTempPath(), $"eggincognito-ca-{device.Id}.sh");
        var script = (installScriptTemplate ?? DefaultScript)
            .Replace("{hash}", hash)
            .Replace("{pem_path}", RemotePem)
            .Replace("\r\n", "\n"); // LF only - CRLF breaks the on-device shebang/parse
        try
        {
            await File.WriteAllTextAsync(tmpPem, pem, ct);
            await File.WriteAllTextAsync(tmpScript, script, ct);
        }
        catch (Exception ex) { return (false, $"could not stage files: {ex.Message}"); }

        try
        {
            var pushPem = await Adb(device.Target, ["push", tmpPem, RemotePem], ct);
            if (pushPem.ExitCode != 0) return (false, "push pem failed: " + DeviceParsing.TrimNote(pushPem.Stderr + pushPem.Stdout));
            var pushScript = await Adb(device.Target, ["push", tmpScript, RemoteScript], ct);
            if (pushScript.ExitCode != 0) return (false, "push script failed: " + DeviceParsing.TrimNote(pushScript.Stderr + pushScript.Stdout));
        }
        finally
        {
            try { File.Delete(tmpPem); } catch { }
            try { File.Delete(tmpScript); } catch { }
        }

        // Execute the pushed script as root. Passing the PATH as the single arg to `sh` avoids word-splitting.
        var r = await Adb(device.Target, ["shell", "su", "0", "sh", RemoteScript], ct);
        var diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
        if (r.ExitCode != 0) return (false, $"install rc={r.ExitCode}: {diag}");
        var trusted = r.Stdout.Contains("verify-zygotens: present");
        return (true, $"{hash}.0 ({(trusted ? "VISIBLE to zygote" : "NOT verified - see diag")}): {diag}");
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
