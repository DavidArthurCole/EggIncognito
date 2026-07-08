using System.Security.Cryptography.X509Certificates;

namespace EggIncognito.Core.Services.Devices;

// Installs the capture CA into a ROOTED Android device's SYSTEM trust store over adb, so apps that only
// trust system CAs (Egg Inc ignores the user store) accept the proxy and auxbrain flows decrypt.
// Android 14's read-only cacerts dirs + zygote's separate mount namespace mean the bind-mount must be
// done inside zygote's namespace (PID 1's is SELinux-denied to su on this ROM), so app processes already
// running need a force-restart to pick it up.
//
// The whole script is PUSHED AS A FILE and executed, never passed as `sh -c "..."` (adb word-splits a
// single arg). Every step echoes a `diag` line, surfaced in the install note. Config override:
// DeviceCapture:Android:CaInstallScript.
public sealed class AdbCaInstaller(IProcessRunner runner, string? installScriptTemplate = null) : IDeviceCaInstaller
{
    public string Platform => "android";

    private const string RemoteScript = "/data/local/tmp/eggincognito-ca-magisk.sh";

    // MAGISK-MODULE install (the device is Magisk-rooted): drops a module whose
    // system/etc/security/cacerts/<hash>.0 holds our PEM, which Magisk magic-mounts over the live system CA
    // store (incl the conscrypt APEX on Magisk 26.4+/27+) for every process at boot.
    // PURE FILE OPS only, no `magisk` binary calls: invoking the magisk client from a plain `su 0 sh` context
    // fails 'Cannot connect to daemon' and SIGTRAPs, aborting the script mid-write. Activation needs no daemon
    // (Magisk scans modules at boot), so this returns NEEDS-REBOOT and separately attempts a live mount via
    // Magisk's own cacerts handler to skip the reboot when possible.
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
        // LIVE activation, no reboot: the Magisk module tree is not picked up by the live tmpfs cacerts
        // mount on Android 14, so copy the cert straight into it. Must run in the global mount namespace
        // (su -mm) or it is invisible to apps; idempotent, survives until reboot.
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
        // Base64'd to one line so the PEM survives the command string; `base64 -d` reproduces it exactly.
        var certB64 = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(pem));

        // Pushed as a file (not `sh -c "<script>"`) so $vars/newlines/heredocs survive adb's arg word-splitting.
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

        // Run as root in the global mount namespace (`su -mm`) so the live-cacerts copy is visible to app
        // processes forked from zygote. Single short -c arg avoids adb word-splitting; stderr merged for diag.
        var r = await Adb(device.Target, ["shell", "su", "-mm", "-c", $"sh {RemoteScript} 2>&1"], ct);
        var diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
        if (string.IsNullOrWhiteSpace(diag)) diag = "(no script output - check su works)";
        if (r.ExitCode != 0) return (false, $"install rc={r.ExitCode}: {diag}");
        // Live mount is the real success signal; module write is the persistence backup.
        var live = r.Stdout.Contains("live: mounted");
        var mod = r.Stdout.Contains("module: written");
        return (live, $"{hash}.0 ({(live ? "trusted (live)" : mod ? "module written but live mount FAILED" : "FAILED")}): {diag}");
    }

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", new[] { "-s", serial }.Concat(rest).ToArray(), ct);
}
