namespace EggIncognito.Services.ProtoExtract.Decomp;

// Discovers every visual effect a building spawns + recovers each as an EffectModel, dynamically from the binary
// call graph (no hardcoded per-building effect list). Given a mesh stem, maps it to the building's setup/update
// function, walks the call graph to find effect-creation sites, and symbolically executes each to recover its
// per-frame placement math. Never throws.
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
