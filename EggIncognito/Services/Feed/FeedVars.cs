using System.Text;

namespace EggIncognito.Services.Feed;

public sealed record FeedVarInfo(string Name, string Label, string Example);

public static class FeedVars {
    private const int ExampleCap = 14;

    public static IReadOnlyList<FeedVarInfo> Describe(FeedEventKindInfo kind) {
        var values = Sample(kind.Key);
        var described = new List<FeedVarInfo>(kind.Vars.Count);
        foreach (string name in kind.Vars) {
            described.Add(new FeedVarInfo(name, Label(name),
                values.TryGetValue(name, out string? value) ? Shorten(value) : ""));
        }

        return described;
    }

    public static string Label(string name) {
        if (name.Length == 0) return name;
        var sb = new StringBuilder(name.Length + 4);
        sb.Append(char.ToUpperInvariant(name[0]));
        foreach (char c in name.AsSpan(1)) {
            if (char.IsUpper(c)) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }

        return sb.ToString();
    }

    private static Dictionary<string, string> Sample(string kind) {
        var samples = FeedSamples.For(kind);
        return samples.Count == 0 ? [] : VarsOf(samples[0].Event);
    }

    private static Dictionary<string, string> VarsOf(INotificationEvent source) => source switch {
        ProtoBuildEvent p => FeedTemplate.BuildVars(p.Platform, p.AppVersion, p.Build, p.ClientVersion, p.ProtoSha,
            p.ProtoChanged, p.PageUrl, p.Delta, p.PrevAppVersion, p.PrevBuild, p.FlawList),
        ConfigChangedEvent c => FeedTemplate.ConfigVars(c.Feed, c.FeedLabel, c.Sha, c.PageUrl,
            c.Changed, c.Added, c.Removed),
        GameDataRebuiltEvent g => FeedTemplate.GameDataVars(g.BinaryVersion, g.PrevBinaryVersion, g.Platform,
            g.InputSha, g.ChangedDocs, g.PageUrl),
        _ => []
    };

    private static string Shorten(string value) =>
        value.Length <= ExampleCap ? value : value[..ExampleCap] + "...";
}
