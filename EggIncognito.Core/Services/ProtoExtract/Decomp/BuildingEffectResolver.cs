namespace EggIncognito.Services.ProtoExtract.Decomp;

// Discovers every visual effect a building spawns + recovers each as an EffectModel, DYNAMICALLY from the binary
// call graph. No hardcoded per-building effect list. Given a mesh stem (e.g. "hab_chicken_universe",
// "ei_hatchery_universe"), it maps the stem to the building's setup/update function, walks the call graph to
// find effect-creation sites (particle systems, emitters), and runs the symbolic executor on each to recover its
// per-frame placement math. The renderer drives whatever is found. Never throws.
//
// Phase 1 (call-graph mapping) is informed by the binary probe; this is the entry point the endpoint calls.
public static class BuildingEffectResolver
{
    public static IReadOnlyList<EffectRecovery.EffectModel> Resolve(byte[] bin, string stem)
    {
        if (bin is null || bin.Length < 64 || string.IsNullOrEmpty(stem)) return [];
        try
        {
            return BuildingEffectGraph.DiscoverEffects(bin, stem);
        }
        catch
        {
            return [];
        }
    }
}
