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
        // Stage into /dev (a GLOBAL/shared tmpfs mount visible in EVERY namespace incl zygote's). /data paths
        // are NOT reliably resolvable inside zygote's mount ns (earlier 'No such file' on the bind source), so
        // the bind source must live on a shared mount. /dev is always shared on Android.
        "D=/dev/eggcacerts\n" +
        "rm -rf \"$D\"; mkdir -p \"$D\"\n" +
        "cp /apex/com.android.conscrypt/cacerts/* \"$D/\" 2>/dev/null\n" +
        "cp /system/etc/security/cacerts/* \"$D/\" 2>/dev/null\n" +
        "cp {pem_path} \"$D/{hash}.0\" && chmod 644 \"$D/{hash}.0\" && chown 0:0 \"$D/{hash}.0\"\n" +
        "chcon u:object_r:system_security_cacerts_file:s0 \"$D\"/* 2>&1 | sed 's/^/diag chcon: /'\n" +
        "[ -f \"$D/{hash}.0\" ] && echo 'diag staged: ok' || echo 'diag staged: FAILED'\n" +
        "ZP=$(pidof zygote64 2>/dev/null || pidof zygote 2>/dev/null); echo \"diag zygote-pid: ${ZP:-none}\"\n" +
        "command -v nsenter >/dev/null 2>&1 && echo 'diag nsenter: present' || echo 'diag nsenter: MISSING'\n" +
        // RECON (no mount yet): the cross-namespace mount kept failing 'No such file' on a different path each
        // attempt, so first dump what zygote's ns actually contains, then mount the RIGHT way (tmpfs in-ns,
        // cert decoded from inline base64 so NO host path is needed - the bytes travel in the command string).
        "B64='{cert_b64}'\n" +
        "if [ -n \"$ZP\" ] && command -v nsenter >/dev/null 2>&1; then\n" +
        "  nsenter -t \"$ZP\" -m -- sh -c '" +
        "    echo recon apex-dir:; ls -ld /apex/com.android.conscrypt/cacerts 2>&1; " +
        "    echo recon sys-dir:; ls -ld /system/etc/security/cacerts 2>&1; " +
        "    echo recon dev-shared:; ls -ld /dev/eggcacerts 2>&1; " +
        "    echo recon mounts:; grep cacerts /proc/self/mounts 2>&1 | head -4" +
        "  ' 2>&1 | sed 's/^/diag /'\n" +
        // Attempt the real fix: tmpfs over the apex cacerts IN zygote ns, repopulate from the certs visible
        // THERE, then add ours decoded from inline base64. No cross-ns file dependency.
        "  nsenter -t \"$ZP\" -m -- sh -c \"" +
        "    T=/apex/com.android.conscrypt/cacerts; " +
        "    cp \\$T/* /dev/.eggorig 2>/dev/null; mkdir -p /dev/.eggorig; cp \\$T/* /dev/.eggorig/ 2>/dev/null; " +
        "    mount -t tmpfs tmpfs \\$T 2>&1; " +
        "    cp /dev/.eggorig/* \\$T/ 2>/dev/null; " +
        "    echo '$B64' | base64 -d > \\$T/{hash}.0 2>&1; chmod 644 \\$T/{hash}.0; " +
        "    chcon u:object_r:system_security_cacerts_file:s0 \\$T/{hash}.0 2>/dev/null; " +
        "    [ -f \\$T/{hash}.0 ] && echo present || echo absent" +
        "  \" 2>&1 | sed 's/^/diag tmpfs-inns: /'\n" +
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
        // The Android system store parses PEM, so the inline-injected cert (decoded in zygote's ns) is the
        // PEM, base64'd to one line so it survives the command string + `base64 -d` reproduces the PEM exactly.
        var certB64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(pem));

        // Stage PEM + script as local temp files and push both (a pushed script preserves $vars + newlines,
        // which `adb shell su 0 sh -c "<script>"` does NOT - adb word-splits the one arg).
        var tmpPem = Path.Combine(Path.GetTempPath(), $"eggincognito-ca-{device.Id}.pem");
        var tmpScript = Path.Combine(Path.GetTempPath(), $"eggincognito-ca-{device.Id}.sh");
        var script = (installScriptTemplate ?? DefaultScript)
            .Replace("{hash}", hash)
            .Replace("{pem_path}", RemotePem)
            .Replace("{cert_b64}", certB64)
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

        // Execute the pushed script as root. `su 0 sh <file>` produced NO output on this ROM (the arg after
        // `sh` is dropped by this su), so wrap the exec in `su 0 sh -c "sh <path> 2>&1"`: `-c` takes one short
        // arg (no spaces in the path => no word-split), runs the file, and merges stderr so diag is captured.
        var r = await Adb(device.Target, ["shell", "su", "0", "sh", "-c", $"sh {RemoteScript} 2>&1"], ct);
        var diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
        if (string.IsNullOrWhiteSpace(diag)) diag = "(no script output - check the script pushed + su works)";
        if (r.ExitCode != 0) return (false, $"install rc={r.ExitCode}: {diag}");
        var trusted = r.Stdout.Contains("verify-zygotens: present");
        return (true, $"{hash}.0 ({(trusted ? "VISIBLE to zygote" : "NOT verified - see diag")}): {diag}");
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
