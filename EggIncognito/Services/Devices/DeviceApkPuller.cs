using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

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
