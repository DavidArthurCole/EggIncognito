using System.Text.RegularExpressions;

namespace EggIncognito.Services.Feed;

// Substitutes {{variable}} tokens in a user-authored subscription message. Simple string-replace, no
// template engine: the variable set is small and fixed (FeedDispatcher's runtime fields).
public static partial class FeedTemplate
{
    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TokenPattern();

    // Unknown/unresolved tokens are left as-is (not blanked) so a typo is visible in the sent message.
    public static string Render(string template, IReadOnlyDictionary<string, string> vars) =>
        TokenPattern().Replace(template, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    public static Dictionary<string, string> BuildVars(
        string platform, string appVersion, string build, string? clientVersion, string protoSha,
        bool protoChanged, string pageUrl) => new()
    {
        ["platform"] = platform,
        ["appVersion"] = appVersion,
        ["build"] = build,
        ["clientVersion"] = clientVersion ?? "",
        ["protoSha"] = protoSha,
        ["protoChanged"] = protoChanged ? "changed" : "unchanged",
        ["pageUrl"] = pageUrl,
    };
}
