namespace EggIncognito.GameData;

public interface IDimensionCatalog {
    IReadOnlyList<string> Dimensions { get; }
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    bool Contains(string id);
}

public sealed class DimensionCatalog : IDimensionCatalog {
    private readonly HashSet<string> _ids;

    private DimensionCatalog(IReadOnlyList<string> dimensions, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Dimensions = dimensions;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _ids = new HashSet<string>(dimensions, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Dimensions { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public bool Contains(string id) => _ids.Contains(id);

    public static DimensionCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<DimensionCatalogDataFile>(json, "Dimension catalog");
        if (file.Dimensions is null) throw new GameDataSchemaException("Dimension catalog missing dimensions.");
        foreach (string id in file.Dimensions) {
            if (string.IsNullOrEmpty(id))
                throw new GameDataSchemaException("Dimension catalog contains an empty id.");
        }

        return new DimensionCatalog(file.Dimensions, file.BinaryVersion ?? "",
            file.Provenance ?? GameData.Provenance.Empty);
    }
}

public sealed record DimensionCatalogDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<string> Dimensions);
