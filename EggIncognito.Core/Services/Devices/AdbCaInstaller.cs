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

    private const string RemoteScript = "/data/local/tmp/eggincognito-ca-magisk.sh";

    // MAGISK-MODULE install (the device is Magisk-rooted). Manual zygote-namespace mounting fought Android 14's
    // apex isolation for many rounds; Magisk's magic-mount is purpose-built for exactly this and propagates the
    // overlay to EVERY process incl zygote, persistently across reboots. We drop a module whose
    // system/etc/security/cacerts/<hash>.0 holds our PEM; Magisk overlays it onto the live system CA store.
    //
    // Modern Magisk (26.4+/27+) also injects into the conscrypt APEX cacerts, so Android 14 picks it up. The
    // cert PEM is decoded from inline base64 (travels in the script, no fragile cross-namespace file path).
    // Activation: Magisk applies modules at boot, so this returns NEEDS-REBOOT; we also attempt a live mount
    // via Magisk's own cacerts handler when available so a reboot can be skipped. Reports each step as `diag`.
    // Writes a Magisk module - PURE FILE OPS only, no `magisk` binary calls. Invoking the magisk client from a
    // plain `su 0 sh` context fails 'Cannot connect to daemon' and SIGTRAPs (rc 133), aborting the script and
    // leaving the module half-written. Module activation needs NO daemon: Magisk scans /data/adb/modules at
    // boot and magic-mounts each module's system/ tree (incl the conscrypt APEX cacerts on Magisk 26.4+/27+)
    // over the real filesystem for every process. So we only drop the files + report; activation is the reboot.
    private const string DefaultScript =
        "#!/system/bin/sh\n" +
        "MODID=eggincognito-ca\n" +
        "MOD=/data/adb/modules/$MODID\n" +
        "mkdir -p $MOD/system/etc/security/cacerts || { echo 'diag module: mkdir FAILED (no /data/adb/modules - is this Magisk?)'; exit 0; }\n" +
        "cat > $MOD/module.prop <<EOF\n" +
        "id=$MODID\n" +
        "name=EggIncognito Capture CA\n" +
        "version=1\n" +
        "versionCode=1\n" +
        "author=eggincognito\n" +
        "description=Trusts the EggIncognito capture root CA as a system CA for traffic capture.\n" +
        "EOF\n" +
        "echo '{cert_b64}' | base64 -d > $MOD/system/etc/security/cacerts/{hash}.0\n" +
        "chmod 644 $MOD/system/etc/security/cacerts/{hash}.0\n" +
        "chcon u:object_r:system_security_cacerts_file:s0 $MOD/system/etc/security/cacerts/{hash}.0 2>/dev/null\n" +
        "rm -f $MOD/disable $MOD/remove 2>/dev/null\n" + // ensure the module is enabled
        "[ -f $MOD/system/etc/security/cacerts/{hash}.0 ] && echo 'diag module: written' || echo 'diag module: FAILED'\n" +
        // LIVE activation, no reboot. On Android 14 the Magisk module's system/ overlay does NOT land in the
        // live cacerts store (Magisk re-mounts /system/etc/security/cacerts as a tmpfs with per-cert bind
        // mounts; the module tree is not picked up there). So copy our cert straight into the LIVE tmpfs
        // cacerts. This MUST run in the GLOBAL mount namespace (su -mm) or it is invisible to apps - the caller
        // invokes the script via `su -mm`. Idempotent: re-copies each run, survives until reboot; the module
        // remains as the (best-effort) persistence attempt. Fix the SELinux context or the app rejects it.
        "LIVE=/system/etc/security/cacerts/{hash}.0\n" +
        "cp $MOD/system/etc/security/cacerts/{hash}.0 $LIVE 2>/dev/null && chown 0:0 $LIVE && chmod 644 $LIVE && chcon u:object_r:system_security_cacerts_file:s0 $LIVE 2>/dev/null && echo 'diag live: mounted into running cacerts' || echo 'diag live: copy FAILED (need su -mm global ns)'\n" +
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

        // The cert travels inline (base64 in the script), so only the script file is pushed. A pushed file
        // preserves $vars + newlines + the heredoc, which `adb shell su 0 sh -c "<script>"` mangles (adb
        // word-splits the single arg).
        var tmpScript = Path.Combine(Path.GetTempPath(), $"eggincognito-ca-{device.Id}.sh");
        var script = (installScriptTemplate ?? DefaultScript)
            .Replace("{hash}", hash)
            .Replace("{cert_b64}", certB64)
            .Replace("\r\n", "\n"); // LF only - CRLF breaks the on-device shebang/parse
        try { await File.WriteAllTextAsync(tmpScript, script, ct); }
        catch (Exception ex) { return (false, $"could not stage script: {ex.Message}"); }

        try
        {
            var pushScript = await Adb(device.Target, ["push", tmpScript, RemoteScript], ct);
            if (pushScript.ExitCode != 0) return (false, "push script failed: " + DeviceParsing.TrimNote(pushScript.Stderr + pushScript.Stdout));
        }
        finally { try { File.Delete(tmpScript); } catch { } }

        // Run as root in the GLOBAL mount namespace (`su -mm`): the script's live-cacerts copy must be visible
        // to app processes (forked from zygote), which a per-shell namespace would hide. `-mm` = mount-master.
        // Single short -c arg (no spaces => adb won't word-split) + merged stderr so the diag is captured.
        var r = await Adb(device.Target, ["shell", "su", "-mm", "-c", $"sh {RemoteScript} 2>&1"], ct);
        var diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
        if (string.IsNullOrWhiteSpace(diag)) diag = "(no script output - check su works)";
        if (r.ExitCode != 0) return (false, $"install rc={r.ExitCode}: {diag}");
        // Live mount is the real success signal (works now, no reboot); module write is the persistence backup.
        var live = r.Stdout.Contains("live: mounted");
        var mod = r.Stdout.Contains("module: written");
        return (live, $"{hash}.0 ({(live ? "trusted (live)" : mod ? "module written but live mount FAILED" : "FAILED")}): {diag}");
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
