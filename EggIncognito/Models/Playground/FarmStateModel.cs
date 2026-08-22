using EggIncognito.Core.Services.Farm;

namespace EggIncognito.Models.Playground;

public sealed class FarmStateModel {
    public int[] Habs { get; set; } = [0, FarmState.EmptyHabTier, FarmState.EmptyHabTier, FarmState.EmptyHabTier];
    public int SilosOwned { get; set; }
    public string SiloAssetType { get; set; } = "Silo0Small";
    public string EggType { get; set; } = "Edible";
    public int LabTier { get; set; }
    public int DepotTier { get; set; }
    public int HoaTier { get; set; }
    public int MissionControlLevel { get; set; }
    public int FuelTankTier { get; set; }
    public bool HyperloopStation { get; set; }
    public bool HyperloopUnderConstruction { get; set; }
    public bool ArtifactsEnabled { get; set; }
    public bool HomeFarm { get; set; } = true;
    public bool FuelTankUnlocked { get; set; }
    public bool HasUnreadMail { get; set; }
}
