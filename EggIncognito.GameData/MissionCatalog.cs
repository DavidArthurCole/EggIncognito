namespace EggIncognito.GameData;

public sealed record MissionCatalogEntry(
    string Id,
    string? DisplayName,
    string Goal);

public interface IMissionCatalog {
    IReadOnlyList<MissionCatalogEntry> Missions { get; }
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
    MissionCatalogEntry? Find(string id);
}

public sealed class MissionCatalog : IMissionCatalog {
    private readonly Dictionary<string, MissionCatalogEntry> _byId;

    private MissionCatalog(IReadOnlyList<MissionCatalogEntry> missions, string binaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Missions = missions;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _byId = missions.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<MissionCatalogEntry> Missions { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public MissionCatalogEntry? Find(string id) => _byId.GetValueOrDefault(id);

    public static MissionCatalog Parse(string json) {
        var file = GameDataJson.Deserialize<MissionCatalogDataFile>(json, "Mission catalog");
        if (file.Missions is null) throw new GameDataSchemaException("Mission catalog missing missions.");
        var missions = file.Missions.Select(ToEntry).ToArray();
        return new MissionCatalog(missions, file.BinaryVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static MissionCatalogEntry ToEntry(MissionCatalogRow row) {
        return string.IsNullOrEmpty(row.Id)
            ? throw new GameDataSchemaException("Mission catalog row missing id.")
            : string.IsNullOrEmpty(row.Goal)
                ? throw new GameDataSchemaException($"Mission catalog '{row.Id}' missing goal.")
                : new MissionCatalogEntry(row.Id, row.DisplayName, row.Goal);
    }
}

public sealed record MissionCatalogRow(
    string? Id,
    string? DisplayName,
    string? Goal);

public sealed record MissionCatalogDataFile(
    string? BinaryVersion,
    IReadOnlyDictionary<string, ProvenanceSource>? Provenance,
    IReadOnlyList<MissionCatalogRow> Missions);
