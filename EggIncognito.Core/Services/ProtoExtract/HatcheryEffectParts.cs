namespace EggIncognito.Services.ProtoExtract;

// Groups the hatchery mesh pieces of one tier into a body + its floating sub-pieces, from the bundle's `.rpo`
// stem list. A hatchery's "floating effect" is not a particle system: it is separate sub-meshes that hover and
// orbit the body, animated by RPA1 curves. Every tier follows `ei_hatchery_<tier>[_<part>]` naming, so the
// binding is programmatic, no hardcoded per-tier list. Pure string grouping; the caller decodes + animates.
public static class HatcheryEffectParts
{
    public sealed record Parts(string Tier, string? Body, IReadOnlyList<string> Floating);

    private const string Prefix = "ei_hatchery_";

    // Extracted from the observed rpos naming, not hand-curated per tier.
    private static readonly string[] FloatingRoots =
    [
        "bolt", "probe", "ring", "top", "middle", "orb",
    ];

    // The tier of a hatchery stem: ei_hatchery_<x> -> <x>, and ei_hatchery_<x>_<floatingSuffix> -> <x>. Returns
    // null for non-hatchery.
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

    // For a requested tier, body + its floating parts: stems whose remainder names a floating component.
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

    // Every distinct hatchery tier present in the stem list.
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
