namespace EggIncognito.Services.ProtoExtract;

// Groups the hatchery mesh pieces of one tier into a body + its floating sub-pieces, from the bundle's `.rpo`
// stem list. The universe hatchery's "floating effect" is NOT a particle system: it is separate sub-meshes
// (ei_hatchery_universe_bolt, ei_hatchery_universe_probe, the darkmatter rings, the ai/vision tops) that hover
// and orbit around the body, animated by RPA1 curves. Every tier follows the same `ei_hatchery_<tier>[_<part>]`
// naming, so the binding is programmatic: no hardcoded per-tier list.
//
// Body = the bare `ei_hatchery_<tier>` stem. Floating parts = `ei_hatchery_<tier>_<suffix>` whose suffix names a
// known floating component (bolt/probe/ring*/top*/middle/orb/...). Other suffixes (rebuild/calm/icons) are not
// meshes in the rpos set, so they never appear here. Pure string grouping; the caller decodes + animates.
public static class HatcheryEffectParts
{
    public sealed record Parts(string Tier, string? Body, IReadOnlyList<string> Floating);

    private const string Prefix = "ei_hatchery_";

    // Suffix roots that denote a floating sub-piece (matched against the part after the tier, ignoring a trailing
    // _<n> index). Extracted from the observed rpos naming, not hand-curated per tier.
    // suffix roots that denote a floating sub-piece. "vision" is a TIER not a part, so it is not here; its parts
    // are ei_hatchery_vision_middle / _top, caught by middle/top.
    private static readonly string[] FloatingRoots =
    [
        "bolt", "probe", "ring", "top", "middle", "orb",
    ];

    // The tier of a hatchery stem. We can only know the tier authoritatively when a body stem exists, so Tiers()
    // derives tiers from the BODIES present (a stem is a body when no other-stem prefix explains it). For a lone
    // stem: ei_hatchery_<x> -> <x>, and ei_hatchery_<x>_<floatingSuffix> -> <x>. Returns null for non-hatchery.
    public static string? TierOf(string stem)
    {
        if (!stem.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var rest = stem[Prefix.Length..];
        if (rest.Length == 0) return null;
        // strip a trailing known floating suffix if present, else the whole remainder is the tier.
        foreach (var root in FloatingRoots)
        {
            var marker = "_" + root;
            var idx = rest.IndexOf(marker, StringComparison.Ordinal);
            if (idx > 0 && IsFloatingSuffix(rest[(idx + 1)..]))
                return rest[..idx];
        }
        return rest;
    }

    // Group all hatchery stems by tier. For a requested tier, body + its floating parts; the floating list is the
    // stems whose remainder (after the tier) names a floating component.
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

    // Every distinct hatchery tier present in the stem list (universe, darkmatter, ai, vision, ...).
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
