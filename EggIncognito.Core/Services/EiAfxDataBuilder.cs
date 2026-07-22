using System.Text;

namespace EggIncognito.Services;

public sealed record ProvenanceSource(string Origin, string? Locator = null, string? Method = null);

public sealed record EiAfxData(
    IReadOnlyDictionary<string, ProvenanceSource> Provenance,
    IReadOnlyList<EiAfxFamily> ArtifactFamilies);

public sealed record EiAfxFamily(
    string Id,
    string SpecName,
    int AfxId,
    int AfxType,
    string Type,
    int SortKey,
    IReadOnlyList<int> ChildAfxIds,
    IReadOnlyList<EiAfxTier> Tiers);

public sealed record EiAfxTier(
    string Id,
    string SpecName,
    int AfxId,
    int AfxLevel,
    int TierNumber,
    string? IconFilename,
    double Quality,
    double Value,
    bool Craftable,
    IReadOnlyList<double> BaseCraftingPrices,
    bool HasRarities,
    IReadOnlyList<int> PossibleAfxRarities);

public static class EiAfxDataBuilder {
    private static readonly IReadOnlyDictionary<string, ProvenanceSource> DefaultProvenance =
        new Dictionary<string, ProvenanceSource>(StringComparer.Ordinal) {
            ["families"] = new ProvenanceSource("fixture", "ei_afx/config", "captured"),
            ["icons"] = new ProvenanceSource("config", "ei/get_config"),
        };

    private static readonly HashSet<string> IngredientNames =
    [
        "EXTRATERRESTRIAL_ALUMINUM", "ANCIENT_TUNGSTEN", "SPACE_ROCKS", "ALIEN_WOOD",
        "GOLD_METEORITE", "TAU_CETI_GEODE", "CENTAURIAN_STEEL", "ERIDANI_FEATHER",
        "DRONE_PARTS", "CELESTIAL_BRONZE", "LALANDE_HIDE", "SOLAR_TITANIUM",
    ];

    public static EiAfxData Build(
        Ei.ArtifactsConfigurationResponse cfg,
        IReadOnlyDictionary<string, string> icons) {
        var families = cfg.ArtifactParameters
            .Where(p => p.Spec is not null)
            .GroupBy(p => p.Spec.Name)
            .OrderBy(g => (int)g.Key)
            .Select(g => BuildFamily(g.Key, [.. g], icons))
            .ToList();

        return new EiAfxData(DefaultProvenance, families);
    }

    public static EiAfxData BuildFromJson(
        string configJson,
        IReadOnlyDictionary<string, string> icons) =>
        Build(Ei.ArtifactsConfigurationResponse.Parser.ParseJson(configJson), icons);

    private static EiAfxFamily BuildFamily(
        Ei.ArtifactSpec.Types.Name name,
        List<Ei.ArtifactsConfigurationResponse.Types.ArtifactParameters> rows,
        IReadOnlyDictionary<string, string> icons) {
        var afxId = (int)name;
        var screaming = Screaming(name);
        var id = screaming.ToLowerInvariant().Replace('_', '-');
        var (afxType, typeName) = Classify(screaming);

        var tiers = rows
            .GroupBy(r => r.Spec.Level)
            .OrderBy(g => (int)g.Key)
            .Select(g => BuildTier(screaming, id, afxId, g.Key, [.. g], icons))
            .ToList();

        return new EiAfxFamily(id, screaming, afxId, afxType, typeName, afxId, [afxId], tiers);
    }

    private static EiAfxTier BuildTier(
        string screaming, string familyId, int afxId,
        Ei.ArtifactSpec.Types.Level level,
        List<Ei.ArtifactsConfigurationResponse.Types.ArtifactParameters> rows,
        IReadOnlyDictionary<string, string> icons) {
        var levelInt = (int)level;
        var tierNumber = levelInt + 1;

        var rarities = rows.Select(r => (int)r.Spec.Rarity).Distinct().OrderBy(r => r).ToList();
        var byRarity = rows.ToDictionary(r => (int)r.Spec.Rarity, r => r);
        var baseRow = byRarity.TryGetValue(0, out var common) ? common : rows[0];
        var prices = rarities.Select(r => byRarity[r].CraftingPrice).ToList();

        var stem = $"afx_{screaming.ToLowerInvariant()}_{tierNumber}";
        var icon = icons.TryGetValue(stem, out var fn) ? fn : null;

        return new EiAfxTier(
            Id: $"{familyId}-{tierNumber}",
            SpecName: screaming,
            AfxId: afxId,
            AfxLevel: levelInt,
            TierNumber: tierNumber,
            IconFilename: icon,
            Quality: baseRow.BaseQuality,
            Value: baseRow.Value,
            Craftable: baseRow.CraftingPrice > 0,
            BaseCraftingPrices: prices,
            HasRarities: rarities.Count > 1,
            PossibleAfxRarities: rarities);
    }

    private static (int AfxType, string Name) Classify(string screaming) {
        if (screaming.EndsWith("_STONE_FRAGMENT", StringComparison.Ordinal)) return (3, "Stone_Ingredient");
        if (screaming.EndsWith("_STONE", StringComparison.Ordinal)) return (1, "Stone");
        if (IngredientNames.Contains(screaming)) return (2, "Ingredient");
        return (0, "Artifact");
    }

    private static string Screaming(Ei.ArtifactSpec.Types.Name name) {
        var pascal = name.ToString();
        var sb = new StringBuilder(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++) {
            var c = pascal[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
