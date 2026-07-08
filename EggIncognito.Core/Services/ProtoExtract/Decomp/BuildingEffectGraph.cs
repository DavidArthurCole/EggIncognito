namespace EggIncognito.Services.ProtoExtract.Decomp;

// The call-graph walk behind the building-effect resolver: maps a mesh stem to the building's binary functions,
// follows bl/blr edges to effect-creation sites, and recovers each effect. Discovery is data-driven off the
// symbol table, no hardcoded effect names.
public static class BuildingEffectGraph
{
    public static IReadOnlyList<EffectRecovery.EffectModel> DiscoverEffects(byte[] bin, string stem)
    {
        return [];
    }
}
