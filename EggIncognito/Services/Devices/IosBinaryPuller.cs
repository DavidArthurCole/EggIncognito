using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Pulls the Egg Inc Mach-O binary off a plugged-in jailbroken iPhone over ssh, in-process via the
// IProcessRunner seam. The App Store binary is FairPlay-encrypted, but only its FIRST __TEXT page
// (cryptsize 4096) is encrypted; the embedded FileDescriptorProto blobs live far past that window in
// __DATA, so the on-disk binary carves WITHOUT any runtime decrypt (dumpdecrypted/frida not needed).
// Verified on egginc 1.36.0.2 (cryptid=1, cryptsize=0x1000, .proto strings at 0x22b7430+).
//
// Flow: ssh `find` the .app bundle by bundle id (Bundle/Application contains randomized UUID dirs), then
// scp the main binary back, read the bytes. Returns null on any failure so Save degrades cleanly.
//
// device.Target = ssh host (the phone IP); ssh creds come from DeviceUpdate:Ios config (SshPort/SshKeyPath),
// reused so the host wiring lives in one place. device.Package = bundle id (com.auxbrain.egginc).
public sealed class IosBinaryPuller(IProcessRunner runner, string sshHost, string sshPort, string sshKeyPath)
{
    public async Task<byte[]?> PullBinaryAsync(string bundleId, CancellationToken ct)
    {
        // Locate the .app bundle. find by Info.plist's bundle id is most robust, but the dir name is the
        // app name not the bundle id; grep the Info.plist for the bundle id, then take that bundle's binary
        // (CFBundleExecutable). One ssh round-trip via a small shell snippet keeps it simple.
        var locate = await Ssh(
            $"for app in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"if grep -qa {Shell(bundleId)} \"$app/Info.plist\" 2>/dev/null; then " +
            $"exe=$(plutil -key CFBundleExecutable \"$app/Info.plist\" 2>/dev/null || defaults read \"$app/Info\" CFBundleExecutable 2>/dev/null); " +
            $"if [ -n \"$exe\" ] && [ -f \"$app/$exe\" ]; then echo \"$app/$exe\"; break; fi; " +
            // fallback: the binary commonly shares the .app stem (egginc.app/egginc)
            $"base=$(basename \"$app\" .app); [ -f \"$app/$base\" ] && echo \"$app/$base\" && break; " +
            $"fi; done", ct);
        if (locate.ExitCode != 0) return null;
        var binPath = locate.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(binPath)) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-ios-{Guid.NewGuid():N}.bin");
        try
        {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{binPath}", dest], ct);
            if (scp.ExitCode != 0 || !File.Exists(dest)) return null;
            return await File.ReadAllBytesAsync(dest, ct);
        }
        finally
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* best-effort */ }
        }
    }

    private Task<ProcessResult> Ssh(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh",
            ["-p", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{sshHost}", remoteCmd], ct);

    // Single-quote for the remote shell, escaping embedded quotes. bundleId is config-controlled, but quote
    // it anyway so it never breaks the find snippet.
    private static string Shell(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
