using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Pulls the Egg Inc 3D ship meshes (.rpo/.rpoz) off a plugged-in jailbroken iPhone over ssh. On a rooted
// device the .app bundle is decrypted on disk, so the mesh files are read directly - no .ipa, no FairPlay
// decrypt. (Contrast IosBinaryPuller, which pulls only the Mach-O for proto carving.)
//
// Flow: ssh-locate the .app bundle by bundle id, find every .rpo/.rpoz under it, tar them on-device into a
// temp file, scp the tarball back (scp moves the bytes intact - the IProcessRunner seam decodes stdout as
// text, so piping a binary tar through it would corrupt it), then TarReader -> RpoAssetExtractor. Returns
// the raw tar bytes; the caller decodes. Returns null on any failure so the device flow degrades cleanly.
//
// device.Target = ssh host (phone IP); creds from DeviceUpdate:Ios config, reused from IosBinaryPuller.
public sealed class IosAssetPuller(IProcessRunner runner, string sshHost, string sshPort, string sshKeyPath)
{
    private const string RemoteTar = "/tmp/egi-rpos.tar";

    // Returns the device's rpos tarball bytes, or null. The caller runs TarReader + RpoAssetExtractor.
    public async Task<byte[]?> PullRposTarAsync(string bundleId, CancellationToken ct)
    {
        // Locate the .app bundle (same probe as IosBinaryPuller), then tar every mesh under it into a temp
        // file. find ... -print0 | tar --null -T - keeps paths with spaces intact; BSD tar on iOS accepts
        // -T - (read file list from stdin). Stored relative (-C "$app") so untarred names are bundle-relative.
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
        try
        {
            var scp = await runner.RunAsync("scp",
                ["-P", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
                 $"root@{sshHost}:{RemoteTar}", dest], ct);
            if (scp.ExitCode != 0 || !File.Exists(dest)) return null;
            return await File.ReadAllBytesAsync(dest, ct);
        }
        finally
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* best-effort */ }
            try { await Ssh($"rm -f {RemoteTar}", ct); } catch { /* best-effort cleanup */ }
        }
    }

    private Task<ProcessResult> Ssh(string remoteCmd, CancellationToken ct) =>
        runner.RunAsync("ssh",
            ["-p", sshPort, "-i", sshKeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{sshHost}", remoteCmd], ct);

    private static string Shell(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
