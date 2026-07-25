using System.Reflection;
using System.Text.Json;

namespace EggIncognito.GameData;

public sealed record VehicleCatalogEntry(
    int Index,
    string Name,
    long Capacity);

public interface IVehicleCatalog {
    IReadOnlyList<VehicleCatalogEntry> Vehicles { get; }
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    VehicleCatalogEntry? Find(int index);
}

public sealed class VehicleCatalog : IVehicleCatalog {
    private readonly Dictionary<int, VehicleCatalogEntry> _byIndex;

    private VehicleCatalog(IReadOnlyList<VehicleCatalogEntry> vehicles, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Vehicles = vehicles;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _byIndex = vehicles.ToDictionary(v => v.Index);
    }

    public IReadOnlyList<VehicleCatalogEntry> Vehicles { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public VehicleCatalogEntry? Find(int index) => _byIndex.GetValueOrDefault(index);

    public static VehicleCatalog Load(string resource = "vehicles.json") {
        var file = VehicleCatalogDataLoader.Read(resource);
        var vehicles = file.Vehicles.Select(ToEntry).ToArray();
        return new VehicleCatalog(vehicles, file.BinaryVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static VehicleCatalogEntry ToEntry(VehicleCatalogRow row) {
        if (row.Index is null) throw new GameDataSchemaException("Vehicle catalog row missing index.");
        return string.IsNullOrEmpty(row.Name)
            ? throw new GameDataSchemaException($"Vehicle catalog index {row.Index} missing name.")
            : row.Capacity is null or <= 0
                ? throw new GameDataSchemaException($"Vehicle catalog '{row.Name}' missing capacity.")
                : new VehicleCatalogEntry(row.Index.Value, row.Name, row.Capacity.Value);
    }
}

public sealed record VehicleCatalogRow(
    int? Index,
    string? Name,
    long? Capacity);

public sealed record VehicleCatalogDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<VehicleCatalogRow> Vehicles);

public static class VehicleCatalogDataLoader {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static VehicleCatalogDataFile Read(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        string full = assembly.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
                      ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
                           ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<VehicleCatalogDataFile>(stream, Options)
               ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
    }
}
