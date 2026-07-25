using System.Reflection;
using System.Text.Json;

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

public sealed class BoostCatalog : IBoostCatalog {
    private readonly Dictionary<string, BoostCatalogEntry> _byId;

    private BoostCatalog(IReadOnlyList<BoostCatalogEntry> boosts, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Boosts = boosts;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _byId = boosts.ToDictionary(b => b.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<BoostCatalogEntry> Boosts { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public BoostCatalogEntry? Find(string id) => _byId.GetValueOrDefault(id);

    public static BoostCatalog Load(string resource = "boost-catalog.json") {
        var file = BoostCatalogDataLoader.Read(resource);
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

public static class BoostCatalogDataLoader {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static BoostCatalogDataFile Read(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        string full = assembly.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
                      ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
                           ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<BoostCatalogDataFile>(stream, Options)
               ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
    }
}
