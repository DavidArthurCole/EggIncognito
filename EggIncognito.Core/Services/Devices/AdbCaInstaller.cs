using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EggIncognito.Core.Services.Devices;

public sealed class AdbCaInstaller(IProcessRunner runner, string? installScriptTemplate = null) : IDeviceCaInstaller {
    private const string RemoteScript = "/data/local/tmp/eggincognito-ca-magisk.sh";

    private const string DefaultScript =
        """
        #!/system/bin/sh
        MODID=eggincognito-ca
        MOD=/data/adb/modules/$MODID
        CACERTS=/system/etc/security/cacerts
        LIVE=$CACERTS/{hash}.0
        PEM=/data/local/tmp/eggincognito-ca.pem
        SNAP=/data/local/tmp/eggincognito-cacerts-snap
        echo '{cert_b64}' | base64 -d > $PEM
        if [ -d /data/adb/modules ]; then
        mkdir -p $MOD/system/etc/security/cacerts
        cat > $MOD/module.prop <<EOF
        id=$MODID
        name=EggIncognito Capture CA
        version=1
        versionCode=1
        author=eggincognito
        description=Trusts the EggIncognito capture root CA as a system CA for traffic capture.
        EOF
        echo '{cert_b64}' | base64 -d > $MOD/system/etc/security/cacerts/{hash}.0
        chmod 644 $MOD/system/etc/security/cacerts/{hash}.0
        chcon u:object_r:system_security_cacerts_file:s0 $MOD/system/etc/security/cacerts/{hash}.0
        rm -f $MOD/disable $MOD/remove
        [ -f $MOD/system/etc/security/cacerts/{hash}.0 ] && echo 'diag module: written' || echo 'diag module: FAILED'
        else
        echo 'diag module: skipped (no /data/adb/modules)'
        fi
        TOUCH=$(touch $CACERTS/.egi-w 2>&1)
        if [ -f $CACERTS/.egi-w ]; then
        rm -f $CACERTS/.egi-w
        echo 'diag live: store already writable'
        else
        rm -rf $SNAP
        mkdir -p $SNAP
        SNAPERR=$(cp -f $CACERTS/* $SNAP/ 2>&1)
        SNAPN=$(ls $SNAP | wc -l)
        if [ "$SNAPN" -gt 0 ]; then
        if MOUNTERR=$(mount -t tmpfs -o mode=755 tmpfs $CACERTS 2>&1); then
        FILLERR=$(cp -f $SNAP/* $CACERTS/ 2>&1)
        echo "diag live: tmpfs mounted, $SNAPN certs restored"
        [ -n "$FILLERR" ] && echo "diag live: restore errors: $FILLERR"
        else
        echo "diag live: tmpfs mount FAILED: $MOUNTERR $TOUCH"
        fi
        else
        echo "diag live: snapshot FAILED: $SNAPERR $TOUCH"
        fi
        fi
        CPERR=$(cp -f $PEM $LIVE 2>&1)
        PERMERR=$(chown 0:0 $CACERTS $CACERTS/* 2>&1; chmod 755 $CACERTS 2>&1; chmod 644 $CACERTS/* 2>&1)
        CONERR=$(chcon u:object_r:system_security_cacerts_file:s0 $CACERTS $CACERTS/* 2>&1)
        VERIFY=$(head -n 1 $LIVE 2>&1)
        case "$VERIFY" in
        *"BEGIN CERTIFICATE"*) echo 'diag live: mounted into running cacerts' ;;
        *) echo "diag live: verify FAILED: $VERIFY $CPERR $PERMERR" ;;
        esac
        [ -n "$CONERR" ] && echo "diag live: chcon: $CONERR"
        rm -f $PEM
        rm -rf $SNAP
        echo 'diag done {hash}.0'

        """;

    public string Platform => "android";

    public async Task<(bool Ok, string? Note)>
        InstallAsync(DeviceTarget device, string caPath, CancellationToken ct) {
        if (!File.Exists(caPath)) return (false, $"ca file not found: {caPath}");

        X509Certificate2 cert;
        try {
            cert = X509CertificateLoader.LoadCertificateFromFile(caPath);
        } catch (Exception ex) {
            return (false, $"could not read ca: {ex.Message}");
        }

        string hash = CaCertPrep.AndroidSubjectHashOld(cert);
        string pem = CaCertPrep.ToPem(cert);

        string certB64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(pem));

        string tmpScript = DeviceShell.NewTempPath(".sh");
        string script = (installScriptTemplate ?? DefaultScript)
            .Replace("{hash}", hash)
            .Replace("{cert_b64}", certB64)
            .Replace("\r\n", "\n");
        try {
            await File.WriteAllTextAsync(tmpScript, script, ct);
        } catch (Exception ex) {
            return (false, $"could not stage script: {ex.Message}");
        }

        try {
            var pushScript = await Adb(device.Target, ["push", tmpScript, RemoteScript], ct);
            if (pushScript.ExitCode != 0)
                return (false, "push script failed: " + DeviceParsing.TrimNote(pushScript.Stderr + pushScript.Stdout));
        } finally {
            DeviceShell.TryDelete(tmpScript);
        }

        var idProbe = await Adb(device.Target, ["shell", "id -u"], ct);
        bool alreadyRoot = idProbe.ExitCode == 0 && idProbe.Stdout.Trim() == "0";

        var suProbe = await Adb(device.Target, ["shell", "command -v su"], ct);
        bool hasSu = suProbe.ExitCode == 0 && suProbe.Stdout.Trim().Length > 0;

        var r = hasSu
            ? await Adb(device.Target, ["shell", "su", "-mm", "-c", $"sh {RemoteScript} 2>&1"], ct)
            : await Adb(device.Target, ["shell", $"sh {RemoteScript} 2>&1"], ct);
        string diag = DeviceParsing.TrimNote(r.Stdout + (r.Stderr.Length > 0 ? " | err: " + r.Stderr : ""));
        if (string.IsNullOrWhiteSpace(diag)) {
            diag = hasSu ? "(no script output from su -mm)"
                : alreadyRoot ? "(no script output - no su, ran as uid 0)"
                : "(no script output - no su and adbd is not root)";
        }
        if (r.ExitCode != 0) return (false, $"install rc={r.ExitCode}: {diag}");

        bool live = r.Stdout.Contains("live: mounted");
        bool mod = r.Stdout.Contains("module: written");
        if (live) return (true, $"{hash}.0 (trusted (live)): {diag}");

        string cause = Failures(r.Stdout);
        return (false,
            $"{hash}.0 ({(mod ? "module written but live mount FAILED" : "FAILED")}): "
            + (cause.Length > 0 ? DeviceParsing.TrimNote(cause) : diag));
    }

    private static string Failures(string stdout) =>
        string.Join(" | ", stdout.Split('\n').Select(l => l.Trim()).Where(l => l.Contains("FAILED")));

    private Task<ProcessResult> Adb(string serial, IEnumerable<string> rest, CancellationToken ct) =>
        runner.RunAsync("adb", ["-s", serial, .. rest], ct);
}
