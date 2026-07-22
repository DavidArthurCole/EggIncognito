using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Services;

public static class BoostCatalogBuilder {
    public sealed record BoostCatalogBuildRow(
        string Id,
        string? DisplayName,
        string? Description,
        int? Price,
        int? TokenPrice,
        double? SeRequired,
        string IconAsset);

    public sealed record ProvenanceSource(string Origin, string? Locator = null, string? Method = null);

    public sealed record BoostCatalogBuildFile(
        string BinaryVersion,
        IReadOnlyDictionary<string, ProvenanceSource> Provenance,
        IReadOnlyList<BoostCatalogBuildRow> Boosts);

    public readonly record struct BuildResult(BoostCatalogBuildFile File, IReadOnlyList<string> MissingCosts);

    public static readonly JsonSerializerOptions CamelJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static BuildResult Build(byte[] bin, string configJson, string binaryVersion) {
        var entries = BoostCatalogExtractor.Extract(bin).Entries;
        var costs = BoostCostExtractor.FromConfigJson(configJson);

        var rows = new List<BoostCatalogBuildRow>(entries.Count);
        var missing = new List<string>();

        foreach (var e in entries) {
            var bare = e.Id.EndsWith("_v2", StringComparison.Ordinal) ? e.Id[..^3] : e.Id;
            var iconAsset = "b_icon_" + bare;

            if (costs.TryGetValue(e.Id, out var cost)) {
                rows.Add(new BoostCatalogBuildRow(e.Id, e.DisplayName, e.Description, cost.Price, cost.TokenPrice, cost.SeRequired, iconAsset));
            } else {
                missing.Add(e.Id);
                rows.Add(new BoostCatalogBuildRow(e.Id, e.DisplayName, e.Description, null, null, null, iconAsset));
            }
        }

        var provenance = new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["identity"] = new ProvenanceSource("binary", "boostmanager"),
            ["cost"] = new ProvenanceSource("config", "ei/get_config"),
            ["iconAsset"] = new ProvenanceSource("derived"),
        };
        var file = new BoostCatalogBuildFile(binaryVersion, provenance, rows);
        return new BuildResult(file, missing);
    }

    public static string Serialize(BoostCatalogBuildFile file) => JsonSerializer.Serialize(file, CamelJson);

    public static string BuildJson(byte[] bin, string configJson, string binaryVersion) =>
        Serialize(Build(bin, configJson, binaryVersion).File);
}
