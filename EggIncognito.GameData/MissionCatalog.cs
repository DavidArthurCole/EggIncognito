using System.Reflection;
using System.Text.Json;

namespace EggIncognito.GameData;

public sealed record MissionCatalogEntry(
    string Id,
    string? DisplayName,
    string Goal);

public interface IMissionCatalog {
    IReadOnlyList<MissionCatalogEntry> Missions { get; }
    MissionCatalogEntry? Find(string id);
    string BinaryVersion { get; }
    IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }
}

public sealed class MissionCatalog : IMissionCatalog {
    private readonly Dictionary<string, MissionCatalogEntry> _byId;

    private MissionCatalog(IReadOnlyList<MissionCatalogEntry> missions, string binaryVersion, IReadOnlyDictionary<string, ProvenanceSource> provenance) {
        Missions = missions;
        BinaryVersion = binaryVersion;
        Provenance = provenance;
        _byId = missions.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<MissionCatalogEntry> Missions { get; }
    public string BinaryVersion { get; }
    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; }

    public MissionCatalogEntry? Find(string id) => _byId.GetValueOrDefault(id);

    public static MissionCatalog Load(string resource = "missions.json") {
        var file = MissionCatalogDataLoader.Read(resource);
        var missions = file.Missions.Select(ToEntry).ToArray();
        return new MissionCatalog(missions, file.BinaryVersion ?? "", file.Provenance ?? GameData.Provenance.Empty);
    }

    private static MissionCatalogEntry ToEntry(MissionCatalogRow row) {
        if (string.IsNullOrEmpty(row.Id)) {
            throw new GameDataSchemaException("Mission catalog row missing id.");
        }
        return string.IsNullOrEmpty(row.Goal)
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

public static class MissionCatalogDataLoader {
    private static readonly JsonSerializerOptions Options = new() {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static MissionCatalogDataFile Read(string resourceName) {
        var assembly = Assembly.GetExecutingAssembly();
        var full = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.Ordinal))
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' not found.");

        using var stream = assembly.GetManifestResourceStream(full)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' unreadable.");

        return JsonSerializer.Deserialize<MissionCatalogDataFile>(stream, Options)
            ?? throw new GameDataSchemaException($"Embedded data '{resourceName}' parsed null.");
    }
}
