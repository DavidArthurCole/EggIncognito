namespace EggIncognito.Services.ProtoExtract.Decomp;

public static class BuildingEffectResolver {
    public static IReadOnlyList<EffectRecovery.EffectModel> Resolve(byte[] bin, string stem) {
        if (bin is null || bin.Length < 64 || string.IsNullOrEmpty(stem)) return [];
        try {
            return BuildingEffectGraph.DiscoverEffects();
        } catch {
            return [];
        }
    }
}
