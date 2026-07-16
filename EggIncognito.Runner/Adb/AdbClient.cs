using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EggIncognito.Runner.Adb;
public interface IAdbClient
{
    string DumpsysPackage(string package);

   
    string PullArmApk(string package, string destPath);
}

public sealed class AdbClient : IAdbClient
{
    private readonly string _target;

    public AdbClient(string target) => _target = target;

    private static readonly Regex VersionRe =
        new(@"versionName=([^\s]+)", RegexOptions.Compiled);

    private static readonly Regex VersionCodeRe =
        new(@"versionCode=(\d+)", RegexOptions.Compiled);

    public static string ParseVersionName(string dumpsys)
    {
        var m = VersionRe.Match(dumpsys);
        return m.Success ? m.Groups[1].Value : "";
    }

    public static string ParseVersionCode(string dumpsys)
    {
        var m = VersionCodeRe.Match(dumpsys);
        return m.Success ? m.Groups[1].Value : "";
    }

   
    public static IReadOnlyList<string> ParseApkPaths(string pmPathOutput)
    {
        var paths = new List<string>();
        foreach (var raw in pmPathOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("package:"))
            {
                paths.Add(line["package:".Length..].Trim());
            }
        }
        return paths;
    }

   
    public static string SelectArmApk(IReadOnlyList<string> apkPaths)
    {
        foreach (var p in apkPaths)
        {
            if (p.Contains("arm"))
            {
                return p;
            }
        }
        return "";
    }

    public string DumpsysPackage(string package) =>
        RunText("-s", _target, "shell", "dumpsys", "package", package);

    public string PullArmApk(string package, string destPath)
    {
        var paths = ParseApkPaths(RunText("-s", _target, "shell", "pm", "path", package));
        var arm = SelectArmApk(paths);
        if (arm == "")
        {
            throw new InvalidOperationException(
                $"no arm split found for {package}; pm path returned {paths.Count} entries");
        }
        var r = RunText("-s", _target, "pull", arm, destPath);
        if (!File.Exists(destPath))
        {
            throw new InvalidOperationException($"adb pull did not produce {destPath}: {r}");
        }
        return destPath;
    }

   
    private static string RunText(params string[] args)
    {
        var psi = new ProcessStartInfo("adb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return stdout + stderr;
    }
}
