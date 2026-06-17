using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.Devices;

// Pure parsers for the two probe outputs. Kept separate from the probes so they unit-test without
// spawning a process. Android regexes mirror Runner's AdbClient (Core cannot reference Runner).
public static partial class DeviceParsing
{
    [GeneratedRegex(@"versionName=([^\s]+)")] private static partial Regex VersionNameRe();
    [GeneratedRegex(@"versionCode=(\d+)")] private static partial Regex VersionCodeRe();

    public static (string? AppVersion, string? Build) AndroidVersion(string dumpsys)
    {
        var name = VersionNameRe().Match(dumpsys);
        var code = VersionCodeRe().Match(dumpsys);
        return (name.Success ? name.Groups[1].Value : null,
                code.Success ? code.Groups[1].Value : null);
    }

    // `ideviceinstaller list` prints one CSV line per app: `<bundleId>, "<shortVersion>", "<name>"`.
    // (The Debian-packaged build has no -o xml; the CSV is the only format.) Find the line whose first
    // field is bundleId, return the first quoted value (CFBundleShortVersionString, the app version).
    public static string? IosAppVersion(string listOutput, string bundleId)
    {
        foreach (var raw in listOutput.Split('\n'))
        {
            var line = raw.Trim();
            var comma = line.IndexOf(',');
            if (comma < 0) continue;
            if (line[..comma].Trim() != bundleId) continue;

            var q1 = line.IndexOf('"', comma);
            if (q1 < 0) continue;
            var q2 = line.IndexOf('"', q1 + 1);
            if (q2 < 0) continue;
            return line[(q1 + 1)..q2];
        }
        return null;
    }

    // Bound a shell error blurb before it becomes a probe Note (stderr can be verbose).
    public static string TrimNote(string s) => s.Trim() is { Length: > 200 } t ? t[..200] : s.Trim();
}
