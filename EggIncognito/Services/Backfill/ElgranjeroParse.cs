using System.Text.RegularExpressions;

namespace EggIncognito.Services.Backfill;

public sealed record ElgranjeroVersion(string ClientVersion, string AppVersion, string Build);

// Parses an elgranjero commit subject like "ClientVersion: 72, AppVersion: 1.35.7, Build: 111343".
// Non-version commits (workflow edits, merges) return null and are skipped by the importer.
public static partial class ElgranjeroParse
{
    [GeneratedRegex(@"ClientVersion:\s*(\d+),\s*AppVersion:\s*([\d.]+),\s*Build:\s*(\d+)")]
    private static partial Regex Re();

    public static ElgranjeroVersion? FromMessage(string message)
    {
        var m = Re().Match(message ?? "");
        return m.Success ? new ElgranjeroVersion(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value) : null;
    }
}
