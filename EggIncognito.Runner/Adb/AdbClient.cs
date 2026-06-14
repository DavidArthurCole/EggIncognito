using System.Diagnostics;
using System.Text.RegularExpressions;

namespace EggIncognito.Runner.Adb;

// IAdbClient is the seam that lets the loop run without a real device in tests.
public interface IAdbClient
{
    // DumpsysPackage returns the raw `dumpsys package <pkg>` text.
    string DumpsysPackage(string package);

    // PullArmApk resolves the arm split via `pm path`, pulls it to destPath over
    // ADB, and returns destPath. The arm split is the one that carries the proto
    // descriptors, matching EggIncProtoExtractor's fullextract pattern.
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

    // ParseVersionName pulls the versionName token out of dumpsys output. This is the
    // user-facing appVersion (e.g. 1.35.7), not unique across builds.
    public static string ParseVersionName(string dumpsys)
    {
        var m = VersionRe.Match(dumpsys);
        return m.Success ? m.Groups[1].Value : "";
    }

    // ParseVersionCode pulls the versionCode out of dumpsys output. This is the monotonic
    // build number (e.g. 111343), unique per build, sitting alongside versionName.
    public static string ParseVersionCode(string dumpsys)
    {
        var m = VersionCodeRe.Match(dumpsys);
        return m.Success ? m.Groups[1].Value : "";
    }

    // ParseApkPaths returns the on-device APK paths from `pm path` output. Each
    // line is `package:/data/app/.../base.apk`; the path is everything after the
    // first colon. Hashed split dirs mean these cannot be guessed, only resolved.
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

    // SelectArmApk picks the split whose filename contains "arm". That split holds
    // the native + descriptor payload pbtk needs. Returns empty if none match.
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
        // `adb pull` writes the file itself. The earlier `cat > dest` form failed:
        // Run passed `> dest` as a literal adb arg, so nothing was ever written.
        var r = RunText("-s", _target, "pull", arm, destPath);
        if (!File.Exists(destPath))
        {
            throw new InvalidOperationException($"adb pull did not produce {destPath}: {r}");
        }
        return destPath;
    }

    // RunText runs adb with the given args (no shell, so no redirection or quoting
    // surprises) and returns stdout.
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
