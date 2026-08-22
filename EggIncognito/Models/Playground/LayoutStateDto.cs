namespace EggIncognito.Models.Playground;

public record LayoutStateDto(
    int[]? Habs,
    int SilosOwned,
    string? SiloAssetType,
    string? EggType,
    string? HatcheryAssetType,
    int LabTier,
    int DepotTier,
    int HoaTier,
    int MissionControlLevel,
    int FuelTankTier,
    bool HyperloopStation,
    bool ArtifactsEnabled,
    bool HomeFarm,
    bool FuelTankUnlocked,
    bool HasUnreadMail);
