namespace EggIncognito.GameData;

public enum EffectTarget {
    IHR,
    IHROffline,
    IHRSharing,
    HabCapacity,
    BeaconMult,
    UnlimitedHatchery,
    BoostDuration,
    BoostEffectiveness,
    Earnings,
    AwayEarnings,
    EggLayingRate,
    EggValue,
    CoopEggLaying,
    CoopEarnings,
    FarmValue,
    SoulEggs,
    SoulEggBonus,
    SoulEggCollectionRate,
    SoulMirror,
    ResearchCost,
    None
}

public enum CombineMode {
    Add,
    Mul,
    MulPlusOne
}

public static class Folding {
    public static double Fold(CombineMode mode, double seed, IEnumerable<double> contributions) {
        var value = seed;
        foreach (var c in contributions) {
            value = mode switch {
                CombineMode.Add => value + c,
                CombineMode.Mul => value * c,
                CombineMode.MulPlusOne => value * (1 + c),
                _ => value
            };
        }
        return value;
    }
}
