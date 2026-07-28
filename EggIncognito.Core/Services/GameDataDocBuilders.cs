using System.Text.Json;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

public static class GameDataDocBuilders {
    private static readonly JsonSerializerOptions CamelJson = BoostCatalogBuilder.CamelJson;

    public sealed record DocResult(string Json, int Count, IReadOnlyList<string> Skipped);

    public static DocResult BuildMissions(IReadOnlyList<MissionCatalogExtractor.MissionEntry> entries,
        string binaryVersion) {
        var skipped = entries.Where(e => string.IsNullOrEmpty(e.Goal)).Select(e => e.Id).ToList();
        var rows = entries.Where(e => !string.IsNullOrEmpty(e.Goal))
            .Select(e => new { id = e.Id, displayName = e.DisplayName, goal = e.Goal })
            .ToArray();
        var doc = new {
            missions = rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "missiondata"),
                ["goal"] = new("binary", "missiondata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, skipped);
    }

    public static DocResult BuildEggs(IReadOnlyList<EggCatalogExtractor.EggEntry> entries, string binaryVersion) {
        var rows = entries.Select(e => new { index = e.Index, name = e.Name, baseValue = e.BaseValue }).ToArray();
        var doc = new {
            eggs = rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "eggdata"),
                ["baseValue"] = new("binary", "eggdata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, []);
    }

    public static DocResult BuildVehicles(IReadOnlyList<VehicleCatalogExtractor.VehicleEntry> entries,
        string binaryVersion) {
        var skipped = entries.Where(e => string.IsNullOrEmpty(e.Name))
            .Select(e => e.Index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var rows = entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => new { index = e.Index, name = e.Name, capacity = e.Capacity })
            .ToArray();
        var doc = new {
            vehicles = rows,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "vehicledata"),
                ["capacity"] = new("binary", "vehicledata", "decoded")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), rows.Length, skipped);
    }

    public static DocResult BuildDimensions(IReadOnlyList<string> ids, string binaryVersion) {
        var doc = new {
            dimensions = ids,
            binaryVersion,
            provenance = new Dictionary<string, BoostCatalogBuilder.ProvenanceSource>(StringComparer.Ordinal) {
                ["identity"] = new("binary", "boostmanager")
            }
        };
        return new DocResult(JsonSerializer.Serialize(doc, CamelJson), ids.Count, []);
    }
}
