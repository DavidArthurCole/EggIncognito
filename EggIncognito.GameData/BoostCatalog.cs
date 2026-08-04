namespace EggIncognito.GameData;

public sealed record BoostCatalogEntry(
    string Id,
    string? DisplayName,
    string? Description,
    int? Price,
    int? TokenPrice,
    double? SeRequired,
    string IconAsset);

public interface IBoostCatalog {
    IReadOnlyList<BoostCatalogEntry> Boosts { get; }
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    BoostCatalogEntry? Find(string id);
}

public sealed class BoostCatalog : GameDataCatalog<BoostCatalogEntry, string>, IBoostCatalog {
    private BoostCatalog(IReadOnlyList<BoostCatalogEntry> boosts, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance)
        : base(boosts, binaryVersion, provenance, b => b.Id, StringComparer.Ordinal) {
    }

    public IReadOnlyList<BoostCatalogEntry> Boosts => Entries;
    public string BinaryVersion => Version;

    public BoostCatalogEntry? Find(string id) => FindByKey(id);

    public static BoostCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<BoostCatalogDataFile>(json, "Boost catalog");
        if (file.Boosts is null) throw new GameDataSchemaException("Boost catalog missing boosts.");
        var boosts = file.Boosts.Select(ToEntry).ToArray();
        return new BoostCatalog(boosts, file.BinaryVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static BoostCatalogEntry ToEntry(BoostCatalogRow row) {
        return string.IsNullOrEmpty(row.Id)
            ? throw new GameDataSchemaException("Boost catalog row missing id.")
            : string.IsNullOrEmpty(row.IconAsset)
                ? throw new GameDataSchemaException($"Boost catalog '{row.Id}' missing iconAsset.")
                : new BoostCatalogEntry(
                    row.Id,
                    row.DisplayName,
                    row.Description,
                    row.Price,
                    row.TokenPrice,
                    row.SeRequired,
                    row.IconAsset);
    }
}

public sealed record BoostCatalogRow(
    string? Id,
    string? DisplayName,
    string? Description,
    int? Price,
    int? TokenPrice,
    double? SeRequired,
    string? IconAsset);

public sealed record BoostCatalogDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<BoostCatalogRow> Boosts);
