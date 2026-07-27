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

    public static VehicleCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<VehicleCatalogDataFile>(json, "Vehicle catalog");
        if (file.Vehicles is null) throw new GameDataSchemaException("Vehicle catalog missing vehicles.");
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
