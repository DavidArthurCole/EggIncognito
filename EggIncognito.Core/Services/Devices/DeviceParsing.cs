using System.Text.RegularExpressions;
using System.Xml.Linq;

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

    // The runtime image's `ideviceinstaller -u <udid> -l -o xml` prints a plist <array> of <dict> app
    // entries; find the one whose CFBundleIdentifier matches and return its CFBundleShortVersionString.
    // ideviceinstaller's CLI varies by package build, so fall back to CSV parse if the output is not a plist.
    public static string? IosAppVersion(string output, string bundleId) => IosVersion(output, bundleId).AppVersion;

    // AppVersion = CFBundleShortVersionString (1.36); Build = CFBundleVersion (1.36.0.2).
    public static (string? AppVersion, string? Build) IosVersion(string output, string bundleId)
    {
        var fromPlist = IosFromPlist(output, bundleId);
        if (fromPlist.AppVersion is not null) return fromPlist;
        var csv = IosFromCsv(output, bundleId);
        return (csv, null);
    }

    // plist form: <dict> with alternating <key>/<value> children, one dict per app.
    private static (string? AppVersion, string? Build) IosFromPlist(string xml, string bundleId)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return (null, null); }

        foreach (var dict in doc.Descendants("dict"))
        {
            if (PlistString(dict, "CFBundleIdentifier") == bundleId)
                return (PlistString(dict, "CFBundleShortVersionString"), PlistString(dict, "CFBundleVersion"));
        }
        return (null, null);
    }

    private static string? PlistString(XElement dict, string key)
    {
        var nodes = dict.Elements().ToList();
        for (var i = 0; i < nodes.Count - 1; i++)
        {
            if (nodes[i].Name == "key" && nodes[i].Value == key && nodes[i + 1].Name == "string")
                return nodes[i + 1].Value;
        }
        return null;
    }

    // CSV form: one line per app, `<bundleId>, "<shortVersion>", "<displayName>"`.
    private static string? IosFromCsv(string output, string bundleId)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            var comma = line.IndexOf(',');
            if (comma < 0 || line[..comma].Trim() != bundleId) continue;
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
