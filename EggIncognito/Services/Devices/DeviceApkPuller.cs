using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Pulls the Egg Inc arm split APK off a plugged-in Android device, in-process via the IProcessRunner
// seam (no dependency on EggIncognito.Runner). `pm path` lists the installed split paths; the arm split
// carries the proto descriptors (the others are language/density resources). `adb pull` writes the file,
// we read the bytes. Returns null on any failure so the caller can degrade rather than throw.
public sealed class DeviceApkPuller(IProcessRunner runner)
{
    public async Task<byte[]?> PullArmSplitAsync(string serial, string package, CancellationToken ct)
    {
        var pm = await runner.RunAsync("adb", ["-s", serial, "shell", "pm", "path", package], ct);
        if (pm.ExitCode != 0) return null;

        var arm = SelectArmSplit(pm.Stdout);
        if (arm is null) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-pull-{Guid.NewGuid():N}.apk");
        try
        {
            var pull = await runner.RunAsync("adb", ["-s", serial, "pull", arm, dest], ct);
            if (pull.ExitCode != 0 || !File.Exists(dest)) return null;
            return await File.ReadAllBytesAsync(dest, ct);
        }
        finally
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* best-effort */ }
        }
    }

    // `pm path` prints one `package:/data/app/.../split.apk` per line. Pick the split whose path contains
    // "arm" (the native + descriptor payload); the proto carve needs that one, not base/resource splits.
    internal static string? SelectArmSplit(string pmPathOutput)
    {
        foreach (var raw in pmPathOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("package:")) continue;
            var path = line["package:".Length..].Trim();
            if (path.Contains("arm")) return path;
        }
        return null;
    }
}
