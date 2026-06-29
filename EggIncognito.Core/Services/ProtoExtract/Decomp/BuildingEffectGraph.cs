namespace EggIncognito.Services.ProtoExtract.Decomp;

// The call-graph walk behind the building-effect resolver: map a mesh stem to the building's binary functions,
// follow bl/blr edges to effect-creation sites, and recover each effect. Discovery is data-driven off the symbol
// table (no hardcoded effect names), informed by the binary's effect vocabulary. Pure, never throws upstream.
public static class BuildingEffectGraph
{
    public static IReadOnlyList<EffectRecovery.EffectModel> DiscoverEffects(byte[] bin, string stem)
    {
        // Implemented after the call-graph probe grounds the mapping (stem -> setup fn -> effect sites).
        return [];
    }
}
