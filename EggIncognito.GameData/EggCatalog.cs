namespace EggIncognito.GameData;

public sealed record EggCatalogEntry(
    int Index,
    string? Name,
    double BaseValue);

public interface IEggCatalog {
    IReadOnlyList<EggCatalogEntry> Eggs { get; }
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    EggCatalogEntry? Find(int index);
}

public sealed class EggCatalog : IEggCatalog {
    private readonly Dictionary<int, EggCatalogEntry> _byIndex;

    private EggCatalog(IReadOnlyList<EggCatalogEntry> eggs, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Eggs = eggs;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _byIndex = eggs.ToDictionary(e => e.Index);
    }

    public IReadOnlyList<EggCatalogEntry> Eggs { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public EggCatalogEntry? Find(int index) => _byIndex.GetValueOrDefault(index);

    public static EggCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<EggCatalogDataFile>(json, "Egg catalog");
        if (file.Eggs is null) throw new GameDataSchemaException("Egg catalog missing eggs.");
        var eggs = file.Eggs.Select(ToEntry).ToArray();
        return new EggCatalog(eggs, file.BinaryVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static EggCatalogEntry ToEntry(EggCatalogRow row) {
        return row.Index is null
            ? throw new GameDataSchemaException("Egg catalog row missing index.")
            : row.BaseValue is null
                ? throw new GameDataSchemaException($"Egg catalog index {row.Index} missing baseValue.")
                : new EggCatalogEntry(row.Index.Value, row.Name, row.BaseValue.Value);
    }
}

public sealed record EggCatalogRow(
    int? Index,
    string? Name,
    double? BaseValue);

public sealed record EggCatalogDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<EggCatalogRow> Eggs);
