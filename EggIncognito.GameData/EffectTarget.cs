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
    PortalHabCapacity,
    HatcheryCapacity,
    HatcheryRefillRate,
    RunningChickenBonus,
    RunningChickenBonusCap,
    RunningChickenBonusMult,
    IHRBase,
    VehicleSpeed,
    VehicleCapacity,
    HoverVehicleCapacity,
    VehicleLoadingTime,
    FleetSize,
    HyperloopCarCapacity,
    HyperloopTrainLength,
    SiloTime,
    VehicleCost,
    HabCost,
    EpicResearchCost,
    BoostCost,
    PrestigeEarnings,
    ProphecyEggBonus,
    DroneRewards,
    DroneRewardQuality,
    DroneFrequency,
    GiftRewards,
    VideoDoublerTime,
    HoldToHatchRate,
    HoldToResearch,
    AfxMissionCapacity,
    AfxMissionDuration,
    None
}

public enum CombineMode {
    Add,
    Mul,
    MulPlusOne
}

public static class Folding {
    public static double Fold(CombineMode mode, double seed, IEnumerable<double> contributions) {
        double value = seed;
        foreach (double c in contributions) {
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
