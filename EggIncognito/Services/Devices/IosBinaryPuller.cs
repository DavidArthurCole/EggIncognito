using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;


public sealed class IosBinaryPuller(IProcessRunner runner, string sshHost, string sshPort, string sshKeyPath) {
    public async Task<byte[]?> PullBinaryAsync(string bundleId, CancellationToken ct) {
        var locate = await Ssh(
            $"for app in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"if grep -qa {Shell(bundleId)} \"$app/Info.plist\" 2>/dev/null; then " +
            $"exe=$(plutil -key CFBundleExecutable \"$app/Info.plist\" 2>/dev/null || defaults read \"$app/Info\" CFBundleExecutable 2>/dev/null); " +
            $"if [ -n \"$exe\" ] && [ -f \"$app/$exe\" ]; then echo \"$app/$exe\"; break; fi; " +
            $"base=$(basename \"$app\" .app); [ -f \"$app/$base\" ] && echo \"$app/$base\" && break; " +
            $"fi; done", ct);
        if (locate.ExitCode != 0) return null;
        var binPath = locate.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(binPath)) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-ios-{Guid.NewGuid():N}.bin");
        try {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{binPath}", dest], ct);
            return scp.ExitCode != 0 || !File.Exists(dest) ? null : await File.ReadAllBytesAsync(dest, ct);
        } finally {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
        }
    }

    private Task<ProcessResult> Ssh(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh",
            ["-p", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{sshHost}", remoteCmd], ct);

    private static string Shell(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
