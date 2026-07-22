using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;


public sealed class IosAssetPuller(IProcessRunner runner, string sshHost, string sshPort, string sshKeyPath) {
    private const string RemoteTar = "/tmp/egi-rpos.tar";


    public async Task<byte[]?> PullRposTarAsync(string bundleId, CancellationToken ct) {
        var make = await Ssh(
            $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"grep -qa {Shell(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
            $"[ -z \"$app\" ] && exit 3; " +
            $"cd \"$app\" || exit 4; " +
            $"find . \\( -iname '*.rpo' -o -iname '*.rpoz' \\) -print0 > /tmp/egi-rpos.list 2>/dev/null; " +
            $"[ -s /tmp/egi-rpos.list ] || exit 5; " +
            $"tar --null -cf {RemoteTar} -T /tmp/egi-rpos.list 2>/dev/null || tar -cf {RemoteTar} $(find . \\( -iname '*.rpo' -o -iname '*.rpoz' \\)); " +
            $"rm -f /tmp/egi-rpos.list; [ -s {RemoteTar} ]", ct);
        if (make.ExitCode != 0) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-rpos-{Guid.NewGuid():N}.tar");
        try {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{RemoteTar}", dest], ct);
            return scp.ExitCode != 0 || !File.Exists(dest) ? null : await File.ReadAllBytesAsync(dest, ct);
        } finally {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            try { await Ssh($"rm -f {RemoteTar}", ct); } catch { }
        }
    }


    public async Task<IReadOnlyList<string>> ListRposAsync(string bundleId, CancellationToken ct) {
        var r = await Ssh(
            $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"grep -qa {Shell(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
            $"[ -z \"$app\" ] && exit 3; " +
            $"find \"$app\" \\( -iname '*.rpo' -o -iname '*.rpoz' \\) -exec basename {{}} \\; 2>/dev/null | sort -u", ct);
        return r.ExitCode != 0
            ? []
            : [.. r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StripExt).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];
    }


    public async Task<byte[]?> PullOneRpoAsync(string bundleId, string stem, CancellationToken ct) {
        var find = await Ssh(
            $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"grep -qa {Shell(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
            $"[ -z \"$app\" ] && exit 3; " +
            $"find \"$app\" \\( -name {Shell(stem + ".rpo")} -o -name {Shell(stem + ".rpoz")} \\) 2>/dev/null | head -1", ct);
        if (find.ExitCode != 0) return null;
        var path = find.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(path)) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-rpo-{Guid.NewGuid():N}.bin");
        try {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{path}", dest], ct);
            return scp.ExitCode != 0 || !File.Exists(dest) ? null : await File.ReadAllBytesAsync(dest, ct);
        } finally {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* best-effort */ }
        }
    }


    public async Task<IReadOnlyList<string>> ListTexturesAsync(string bundleId, CancellationToken ct) {
        var r = await Ssh(
            $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"grep -qa {Shell(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
            $"[ -z \"$app\" ] && exit 3; " +
            $"find \"$app\" -iname '*.png' -exec basename {{}} \\; 2>/dev/null | sort -u", ct);
        return r.ExitCode != 0
            ? []
            : [.. r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StripExt).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal)];
    }

    public async Task<byte[]?> PullOneTextureAsync(string bundleId, string stem, CancellationToken ct) {
        var find = await Ssh(
            $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"grep -qa {Shell(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
            $"[ -z \"$app\" ] && exit 3; " +
            $"find \"$app\" -name {Shell(stem + ".png")} 2>/dev/null | head -1", ct);
        if (find.ExitCode != 0) return null;
        var path = find.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(path)) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-png-{Guid.NewGuid():N}.png");
        try {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{path}", dest], ct);
            return scp.ExitCode != 0 || !File.Exists(dest) ? null : await File.ReadAllBytesAsync(dest, ct);
        } finally {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* best-effort */ }
        }
    }

    public async Task<byte[]?> PullAppBinaryAsync(string bundleId, CancellationToken ct) {
        var find = await Ssh(
            $"app=$(for a in /private/var/containers/Bundle/Application/*/*.app; do " +
            $"grep -qa {Shell(bundleId)} \"$a/Info.plist\" 2>/dev/null && echo \"$a\" && break; done); " +
            $"[ -z \"$app\" ] && exit 3; " +
            $"exe=\"$app/$(basename \"$app\" .app)\"; [ -f \"$exe\" ] && echo \"$exe\"", ct);
        if (find.ExitCode != 0) return null;
        var path = find.Stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(path)) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-bin-{Guid.NewGuid():N}.bin");
        try {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{path}", dest], ct);
            return scp.ExitCode != 0 || !File.Exists(dest) ? null : await File.ReadAllBytesAsync(dest, ct);
        } finally {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
        }
    }

    private static string StripExt(string name) {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    private Task<ProcessResult> Ssh(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh",
            ["-p", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{sshHost}", remoteCmd], ct);

    private static string Shell(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
