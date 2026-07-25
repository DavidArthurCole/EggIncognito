using System.Reflection;
using System.Text.Json;

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

    public static DimensionCatalog Load(string resource = "dimensions.json") {
        var file = DimensionCatalogDataLoader.Read(resource);
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

public static class DimensionCatalogDataLoader {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static DimensionCatalogDataFile Read(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        string full = assembly.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
                      ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
                           ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<DimensionCatalogDataFile>(stream, Options)
               ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
    }
}
