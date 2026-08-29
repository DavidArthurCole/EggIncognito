using System.Globalization;
using System.Text.RegularExpressions;
using EggIncognito.Core.Services.ProtoExtract;

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

    public static Dictionary<string, string> ConfigVars(
        string feed, string feedLabel, string sha, string pageUrl,
        IReadOnlyList<string> changed, IReadOnlyList<string> added,
        IReadOnlyList<string> removed) => new() {
            ["feed"] = feed,
            ["feedLabel"] = feedLabel,
            ["sha"] = sha,
            ["pageUrl"] = pageUrl,
            ["changed"] = Joined(changed),
            ["added"] = Joined(added),
            ["removed"] = Joined(removed)
        };

    public static Dictionary<string, string> GameDataVars(
        string binaryVersion, string? prevBinaryVersion, string platform, string inputSha,
        IReadOnlyList<string> changedDocs, string pageUrl) => new() {
            ["binaryVersion"] = binaryVersion,
            ["prevBinaryVersion"] = prevBinaryVersion ?? "",
            ["platform"] = platform,
            ["changedDocs"] = Joined(changedDocs),
            ["docCount"] = changedDocs.Count.ToString(CultureInfo.InvariantCulture),
            ["inputSha"] = inputSha,
            ["pageUrl"] = pageUrl
        };

    private static string Joined(IReadOnlyList<string>? items) =>
        items is null || items.Count == 0 ? "" : string.Join(", ", items);
}
