using System.Text.RegularExpressions;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services.Feed;

public static partial class FeedTemplate {
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern();


    public static string Render(string template, IReadOnlyDictionary<string, string> vars) =>
        TokenPattern().Replace(template, m => vars.TryGetValue(m.Groups[1].Value, out string? v) ? v : m.Value);

    public static IReadOnlyList<string> Tokens(string? template) {
        if (string.IsNullOrEmpty(template)) return [];
        var found = new List<string>();
        foreach (Match match in TokenPattern().Matches(template)) {
            string name = match.Groups[1].Value;
            if (!found.Contains(name)) found.Add(name);
        }

        return found;
    }

    public static Dictionary<string, string> BuildVars(
        string platform, string appVersion, string build, string? clientVersion, string protoSha,
        bool protoChanged, string pageUrl, VersionDelta delta = VersionDelta.Unknown,
        string? prevAppVersion = null, string? prevBuild = null,
        IReadOnlyList<string>? flaws = null) => new() {
            ["platform"] = platform,
            ["appVersion"] = appVersion,
            ["build"] = build,
            ["clientVersion"] = clientVersion ?? "",
            ["protoSha"] = protoSha,
            ["protoChanged"] = protoChanged ? "changed" : "unchanged",
            ["pageUrl"] = pageUrl,
            ["delta"] = VersionDeltaCalc.Label(delta),
            ["prevAppVersion"] = prevAppVersion ?? "",
            ["prevBuild"] = prevBuild ?? "",
            ["flaws"] = Joined(flaws)
        };

    public static Dictionary<string, string> PeriodicalsVars(string feed, string sha, string pageUrl,
        PeriodicalsAspectSummary? aspects = null) => new() {
            ["feed"] = feed,
            ["sha"] = sha,
            ["pageUrl"] = pageUrl,
            ["changedAspects"] = Joined(aspects?.ChangedAspects),
            ["addedEvents"] = Joined(aspects?.AddedEvents),
            ["addedContracts"] = Joined(aspects?.AddedContracts),
            ["addedColleggtibles"] = Joined(aspects?.AddedColleggtibles)
        };

    private static string Joined(IReadOnlyList<string>? items) =>
        items is null || items.Count == 0 ? "" : string.Join(", ", items);
}
