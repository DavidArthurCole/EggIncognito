using Ei;
using Google.Protobuf;

namespace EggIncognito.Core.Services.Farm;

public static class FarmShowcase {
    public sealed record Preset(string Id, string? Name, string Bucket, ShellDB.Types.FarmConfiguration Config);

    public sealed record Result(bool Ok, IReadOnlyList<Preset> Presets, string? Diagnostics);

    public static Result Parse(string? json) {
        if (string.IsNullOrWhiteSpace(json)) return new Result(false, [], "no stored get_shell_showcase fixture");

        ShellShowcase showcase;
        try {
            showcase = ShellShowcase.Parser.ParseJson(json);
        } catch (InvalidProtocolBufferException ex) {
            return new Result(false, [], "get_shell_showcase is not a valid ShellShowcase: " + ex.Message);
        }

        var presets = new List<Preset>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Collect(presets, seen, showcase.Top, "top");
        Collect(presets, seen, showcase.Featured, "featured");
        Collect(presets, seen, showcase.Fresh, "fresh");

        return presets.Count == 0
            ? new Result(false, [], "showcase parsed but carried no farm configurations")
            : new Result(true, presets, null);
    }

    private static void Collect(List<Preset> into, HashSet<string> seen,
        IEnumerable<ShellShowcaseListingInfo> listings, string bucket) {
        foreach (var l in listings) {
            if (l.FarmConfig is null) continue;
            string id = string.IsNullOrEmpty(l.Id) ? l.LocalId : l.Id;
            if (id.Length == 0 || !seen.Add(id)) continue;
            into.Add(new Preset(id, string.IsNullOrEmpty(l.Name) ? null : l.Name, bucket, l.FarmConfig));
        }
    }
}
