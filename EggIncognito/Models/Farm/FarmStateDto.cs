namespace EggIncognito.Models.Farm;

public sealed record FarmStateDto {
    public IReadOnlyList<int>? Habs { get; init; }
    public int? SilosOwned { get; init; }
    public string? SiloAssetType { get; init; }
    public string? EggType { get; init; }
    public int? LabTier { get; init; }
    public int? DepotTier { get; init; }
    public int? HoaTier { get; init; }
    public int? MissionControlLevel { get; init; }
    public int? FuelTankTier { get; init; }
    public bool? HyperloopStation { get; init; }
    public bool? HyperloopUnderConstruction { get; init; }
    public bool? ArtifactsEnabled { get; init; }
    public bool? HomeFarm { get; init; }
    public bool? FuelTankUnlocked { get; init; }
    public bool? HasUnreadMail { get; init; }
    public bool? AllTrophiesComplete { get; init; }
    public IReadOnlyList<int>? EggMedalLevel { get; init; }
}
