namespace EggIncognito.GameData;

public sealed record ArtifactCatalogEntry(
    string Id,
    string SpecName,
    string Level,
    string Rarity,
    int AfxId,
    int AfxLevel,
    int AfxRarity,
    int TierNumber,
    double BaseQuality,
    double OddsMultiplier,
    double Value,
    double CraftingPrice,
    double CraftingPriceLow,
    double CraftingPriceCurve,
    uint CraftingPriceDomain,
    ulong CraftingXp);

public sealed record ArtifactCraftingLevelEntry(int Level, double XpRequired, double RarityMult);

public interface IArtifactCatalog {
    IReadOnlyList<ArtifactCatalogEntry> Artifacts { get; }
    IReadOnlyList<ArtifactCraftingLevelEntry> CraftingLevels { get; }
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    ArtifactCatalogEntry? Find(string id);
}

public sealed class ArtifactCatalog : GameDataCatalog<ArtifactCatalogEntry, string>, IArtifactCatalog {
    public const string DocumentId = "artifact-catalog";

    private ArtifactCatalog(IReadOnlyList<ArtifactCatalogEntry> artifacts,
        IReadOnlyList<ArtifactCraftingLevelEntry> craftingLevels, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance)
        : base(artifacts, binaryVersion, provenance, a => a.Id, StringComparer.Ordinal) {
        CraftingLevels = craftingLevels;
    }

    public IReadOnlyList<ArtifactCatalogEntry> Artifacts => Entries;
    public IReadOnlyList<ArtifactCraftingLevelEntry> CraftingLevels { get; }
    public string BinaryVersion => Version;

    public ArtifactCatalogEntry? Find(string id) => FindByKey(id);

    public static ArtifactCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<ArtifactCatalogDataFile>(json, "Artifact catalog");
        if (file.Artifacts is null) throw new GameDataSchemaException("Artifact catalog missing artifacts.");
        var artifacts = file.Artifacts.Select(ToEntry).ToArray();
        var levels = (file.CraftingLevels ?? []).Select(ToLevel).ToArray();
        return new ArtifactCatalog(artifacts, levels, file.BinaryVersion ?? "",
            file.Provenance ?? GameData.Provenance.Empty);
    }

    private static ArtifactCatalogEntry ToEntry(ArtifactCatalogRow row) {
        if (string.IsNullOrEmpty(row.Id)) throw new GameDataSchemaException("Artifact catalog row missing id.");
        if (string.IsNullOrEmpty(row.SpecName))
            throw new GameDataSchemaException($"Artifact catalog '{row.Id}' missing specName.");
        if (string.IsNullOrEmpty(row.Level))
            throw new GameDataSchemaException($"Artifact catalog '{row.Id}' missing level.");
        if (string.IsNullOrEmpty(row.Rarity))
            throw new GameDataSchemaException($"Artifact catalog '{row.Id}' missing rarity.");
        if (row.CraftingPriceDomain is null or 0)
            throw new GameDataSchemaException($"Artifact catalog '{row.Id}' missing craftingPriceDomain.");

        return new ArtifactCatalogEntry(
            row.Id,
            row.SpecName,
            row.Level,
            row.Rarity,
            row.AfxId ?? 0,
            row.AfxLevel ?? 0,
            row.AfxRarity ?? 0,
            row.TierNumber ?? (row.AfxLevel ?? 0) + 1,
            row.BaseQuality ?? 0,
            row.OddsMultiplier ?? 0,
            row.Value ?? 0,
            row.CraftingPrice ?? 0,
            row.CraftingPriceLow ?? 0,
            row.CraftingPriceCurve ?? 0,
            row.CraftingPriceDomain.Value,
            row.CraftingXp ?? 0);
    }

    private static ArtifactCraftingLevelEntry ToLevel(ArtifactCraftingLevelRow row) =>
        new(row.Level ?? 0, row.XpRequired ?? 0, row.RarityMult ?? 0);
}

public sealed record ArtifactCatalogRow(
    string? Id,
    string? SpecName,
    string? Level,
    string? Rarity,
    int? AfxId,
    int? AfxLevel,
    int? AfxRarity,
    int? TierNumber,
    double? BaseQuality,
    double? OddsMultiplier,
    double? Value,
    double? CraftingPrice,
    double? CraftingPriceLow,
    double? CraftingPriceCurve,
    uint? CraftingPriceDomain,
    ulong? CraftingXp);

public sealed record ArtifactCraftingLevelRow(int? Level, double? XpRequired, double? RarityMult);

public sealed record ArtifactCatalogDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<ArtifactCatalogRow> Artifacts,
    IReadOnlyList<ArtifactCraftingLevelRow>? CraftingLevels);
