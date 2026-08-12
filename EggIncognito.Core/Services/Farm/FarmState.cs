using Ei;

namespace EggIncognito.Core.Services.Farm;

public sealed record FarmState {
    public const int EmptyHabTier = 19;
    public const int HabSlots = 4;
    public const int MaxSilos = 10;

    public IReadOnlyList<int> Habs { get; init; } = [EmptyHabTier, EmptyHabTier, EmptyHabTier, EmptyHabTier];
    public int SilosOwned { get; init; }
    public ShellSpec.Types.AssetType SiloAssetType { get; init; } = ShellSpec.Types.AssetType.Silo0Small;

    public Egg EggType { get; init; } = Egg.Edible;
    public ShellSpec.Types.AssetType HatcheryAssetType { get; init; } = ShellSpec.Types.AssetType.HatcheryEdible;

    public int LabTier { get; init; }
    public int DepotTier { get; init; }
    public int HoaTier { get; init; }
    public int MissionControlLevel { get; init; }
    public int FuelTankTier { get; init; }

    public bool HyperloopStation { get; init; }
    public bool HyperloopUnderConstruction { get; init; }
    public bool ArtifactsEnabled { get; init; }
    public bool HomeFarm { get; init; } = true;
    public bool FuelTankUnlocked { get; init; }
    public bool HasUnreadMail { get; init; }
    public bool ArMode { get; init; }

    public IReadOnlyList<int> EggMedalLevel { get; init; } = [];
    public bool AllTrophiesComplete { get; init; }

    public IReadOnlyList<int> Vehicles { get; init; } = [];
    public IReadOnlyList<int> TrainLength { get; init; } = [];

    public ShellDB.Types.FarmConfiguration? Appearance { get; init; }

    public int HabTier(int slot) =>
        slot >= 0 && slot < Habs.Count ? Habs[slot] : EmptyHabTier;

    public bool HabOccupied(int slot) => HabTier(slot) is >= 0 and < EmptyHabTier;

    public static FarmState FromFarmInfo(PlayerFarmInfo info) {
        var habs = new int[HabSlots];
        for (int i = 0; i < HabSlots; i++)
            habs[i] = i < info.Habs.Count ? (int)info.Habs[i] : EmptyHabTier;

        return new FarmState {
            Habs = habs,
            SilosOwned = (int)info.SilosOwned,
            EggType = info.EggType,
            HatcheryAssetType = HatcheryFor(info.EggType),
            HyperloopStation = info.HyperloopStation,
            EggMedalLevel = [.. info.EggMedalLevel.Select(v => (int)v)],
            Vehicles = [.. info.Vehicles.Select(v => (int)v)],
            TrainLength = [.. info.TrainLength.Select(v => (int)v)],
            Appearance = info.FarmAppearance,
            ArtifactsEnabled = info.EquippedArtifacts.Count > 0
        };
    }

    public static int EggTableIndex(Egg egg) => (int)egg switch {
        >= 1 and <= 19 => (int)egg - 1,
        >= 50 and <= 54 => (int)egg - 50 + 19,
        >= 100 and <= 104 => (int)egg - 100 + 24,
        _ => -1
    };

    public static ShellSpec.Types.AssetType HatcheryFor(Egg egg) => egg switch {
        Egg.Edible => ShellSpec.Types.AssetType.HatcheryEdible,
        Egg.Superfood => ShellSpec.Types.AssetType.HatcherySuperfood,
        Egg.Medical => ShellSpec.Types.AssetType.HatcheryMedical,
        Egg.RocketFuel => ShellSpec.Types.AssetType.HatcheryRocketFuel,
        Egg.SuperMaterial => ShellSpec.Types.AssetType.HatcherySupermaterial,
        Egg.Fusion => ShellSpec.Types.AssetType.HatcheryFusion,
        Egg.Quantum => ShellSpec.Types.AssetType.HatcheryQuantum,
        Egg.Immortality => ShellSpec.Types.AssetType.HatcheryImmortality,
        Egg.Tachyon => ShellSpec.Types.AssetType.HatcheryTachyon,
        Egg.Graviton => ShellSpec.Types.AssetType.HatcheryGraviton,
        Egg.Dilithium => ShellSpec.Types.AssetType.HatcheryDilithium,
        Egg.Prodigy => ShellSpec.Types.AssetType.HatcheryProdigy,
        Egg.Terraform => ShellSpec.Types.AssetType.HatcheryTerraform,
        Egg.Antimatter => ShellSpec.Types.AssetType.HatcheryAntimatter,
        Egg.DarkMatter => ShellSpec.Types.AssetType.HatcheryDarkMatter,
        Egg.Ai => ShellSpec.Types.AssetType.HatcheryAi,
        Egg.Nebula => ShellSpec.Types.AssetType.HatcheryNebula,
        Egg.Universe => ShellSpec.Types.AssetType.HatcheryUniverse,
        Egg.Enlightenment => ShellSpec.Types.AssetType.HatcheryEnlightenment,
        Egg.Chocolate => ShellSpec.Types.AssetType.HatcheryChocolate,
        Egg.Easter => ShellSpec.Types.AssetType.HatcheryEaster,
        Egg.Waterballoon => ShellSpec.Types.AssetType.HatcheryWaterballoon,
        Egg.Firework => ShellSpec.Types.AssetType.HatcheryFirework,
        Egg.Pumpkin => ShellSpec.Types.AssetType.HatcheryPumpkin,
        Egg.Curiosity => ShellSpec.Types.AssetType.HatcheryCuriosity,
        Egg.Integrity => ShellSpec.Types.AssetType.HatcheryIntegrity,
        Egg.Humility => ShellSpec.Types.AssetType.HatcheryHumility,
        Egg.Resilience => ShellSpec.Types.AssetType.HatcheryResilience,
        Egg.Kindness => ShellSpec.Types.AssetType.HatcheryKindness,
        _ => ShellSpec.Types.AssetType.HatcheryCustom
    };
}
