using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

// Pulls Egg Inc APK splits off a plugged-in Android device via adb. Returns null on any failure so the
// caller can degrade rather than throw.
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

    // The 3D ship meshes (`assets/rpos/*.rpoz`) live in the base split, not the arm split.
    public async Task<byte[]?> PullBaseSplitAsync(string serial, string package, CancellationToken ct)
    {
        var pm = await runner.RunAsync("adb", ["-s", serial, "shell", "pm", "path", package], ct);
        if (pm.ExitCode != 0) return null;

        var basePath = SelectBaseSplit(pm.Stdout);
        if (basePath is null) return null;

        var dest = Path.Combine(Path.GetTempPath(), $"egi-pull-{Guid.NewGuid():N}.apk");
        try
        {
            var pull = await runner.RunAsync("adb", ["-s", serial, "pull", basePath, dest], ct);
            if (pull.ExitCode != 0 || !File.Exists(dest)) return null;
            return await File.ReadAllBytesAsync(dest, ct);
        }
        finally
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { /* best-effort */ }
        }
    }

    // Picks the split whose path contains "arm" (the native + proto-descriptor payload).
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

    // Picks the apk named `base.apk`, or the only path when there is no split layout.
    internal static string? SelectBaseSplit(string pmPathOutput)
    {
        string? only = null;
        var count = 0;
        foreach (var raw in pmPathOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("package:")) continue;
            var path = line["package:".Length..].Trim();
            if (path.EndsWith("/base.apk", StringComparison.OrdinalIgnoreCase)) return path;
            only = path; count++;
        }
        return count == 1 ? only : null;
    }
}
