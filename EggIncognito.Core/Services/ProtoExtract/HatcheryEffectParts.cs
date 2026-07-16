namespace EggIncognito.Services.ProtoExtract;

public static class HatcheryEffectParts
{
    public sealed record Parts(string Tier, string? Body, IReadOnlyList<string> Floating);

    private const string Prefix = "ei_hatchery_";

   
    private static readonly string[] FloatingRoots =
    [
        "bolt", "probe", "ring", "top", "middle", "orb",
    ];

   
   
    public static string? TierOf(string stem)
    {
        if (!stem.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var rest = stem[Prefix.Length..];
        if (rest.Length == 0) return null;
        foreach (var root in FloatingRoots)
        {
            var marker = "_" + root;
            var idx = rest.IndexOf(marker, StringComparison.Ordinal);
            if (idx > 0 && IsFloatingSuffix(rest[(idx + 1)..]))
                return rest[..idx];
        }
        return rest;
    }

   
    public static Parts ForTier(IEnumerable<string> stems, string tier)
    {
        string body = Prefix + tier;
        var have = new HashSet<string>(stems, StringComparer.Ordinal);
        var floating = new List<string>();

        foreach (var s in have)
        {
            if (!s.StartsWith(body + "_", StringComparison.Ordinal)) continue;
            var suffix = s[(body.Length + 1)..];
            if (IsFloatingSuffix(suffix)) floating.Add(s);
        }
        floating.Sort(StringComparer.Ordinal);
        return new Parts(tier, have.Contains(body) ? body : null, floating);
    }

   
    public static IReadOnlyList<string> Tiers(IEnumerable<string> stems)
    {
        var tiers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var s in stems)
            if (TierOf(s) is { } t) tiers.Add(t);
        return tiers.ToList();
    }

    private static bool IsFloatingSuffix(string suffix)
    {
        foreach (var root in FloatingRoots)
            if (suffix == root || suffix.StartsWith(root + "_", StringComparison.Ordinal)) return true;
        return false;
    }
}
