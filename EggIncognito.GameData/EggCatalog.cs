using System.Reflection;
using System.Text.Json;

namespace EggIncognito.GameData;

public sealed record EggCatalogEntry(
    int Index,
    string? Name,
    double BaseValue);

public interface IEggCatalog {
    IReadOnlyList<EggCatalogEntry> Eggs { get; }
    EggCatalogEntry? Find(int index);
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
}

public sealed class EggCatalog : IEggCatalog {
    private readonly Dictionary<int, EggCatalogEntry> _byIndex;

    private EggCatalog(IReadOnlyList<EggCatalogEntry> eggs, string binaryVersion, IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Eggs = eggs;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _byIndex = eggs.ToDictionary(e => e.Index);
    }

    public IReadOnlyList<EggCatalogEntry> Eggs { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public EggCatalogEntry? Find(int index) => _byIndex.GetValueOrDefault(index);

    public static EggCatalog Load(string resource = "eggs.json") {
        var file = EggCatalogDataLoader.Read(resource);
        var eggs = file.Eggs.Select(ToEntry).ToArray();
        return new EggCatalog(eggs, file.BinaryVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static EggCatalogEntry ToEntry(EggCatalogRow row) {
        if (row.Index is null) {
            throw new GameDataSchemaException("Egg catalog row missing index.");
        }
        return row.BaseValue is null
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

public static class EggCatalogDataLoader {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static EggCatalogDataFile Read(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        var full = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<EggCatalogDataFile>(stream, Options)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
    }
}
