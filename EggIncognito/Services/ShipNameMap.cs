namespace EggIncognito.Services;

// Maps the game's rpos/ ship mesh file stem (e.g. "ei_ship_chicken_one") to EggLedger's
// MissionInfo.Spaceship enum name (e.g. "ChickenOne"). Explicit, not derived: stems don't normalize
// to enum names (afx shell assets, codenamed bundle ships).
//
// Ships 6-9 (Galeggtica, Chickfiant/Defihent, Voyegger, Henerprise) are CDN-only shells with no
// bundled rpos/ mesh; BundleStem is null until their shell CDN ids resolve.
public static class ShipNameMap
{
    // BundleStem is the rpos/ file stem when the mesh ships in the app, or null for a CDN-only shell.
    // ShellAsset is the afx_ship_* name the CDN fetch keys off.
    public sealed record Ship(int Tier, string EnumName, string Display, string? BundleStem, string? ShellAsset);

    public static readonly IReadOnlyList<Ship> All =
    [
        new(0, "ChickenOne", "Chicken One", "ei_ship_chicken_one", "afx_ship_chicken_1"),
        new(1, "ChickenNine", "Chicken Nine", "ei_ship_chicken_nine", "afx_ship_chicken_9"),
        new(2, "ChickenHeavy", "Chicken Heavy", "ei_ship_chicken_heavy", "afx_ship_chicken_heavy"),
        new(3, "Bcr", "BCR", "ei_ship_bcr", "afx_ship_bcr"),
        new(4, "MilleniumChicken", "Quintillion Chicken", "ei_ship_millenium_chicken", "afx_ship_millenium_chicken"),
        new(5, "CorellihenCorvette", "Cornish-Hen Corvette", "ei_ship_corellihen_corvette", "afx_ship_corellihen_corvette"),
        new(6, "Galeggtica", "Galeggtica", null, "afx_ship_galeggtica"),
        new(7, "Chickfiant", "Defihent", null, "afx_ship_defihent"),
        new(8, "Voyegger", "Voyegger", null, "afx_ship_voyegger"),
        new(9, "Henerprise", "Henerprise", null, "afx_ship_henerprise"),
        new(10, "Atreggies", "Atreggies Henliner", "ei_ship_atreggies_shuttle", "afx_ship_atreggies"),
    ];

    // Lookup by rpos/ file stem. Returns null when the stem is not a bundled ship mesh.
    private static readonly Dictionary<string, string> StemToEnum =
        All.Where(s => s.BundleStem is not null)
           .ToDictionary(s => s.BundleStem!, s => s.EnumName, StringComparer.OrdinalIgnoreCase);

    public static string? EnumNameForStem(string stem) =>
        StemToEnum.TryGetValue(stem, out var e) ? e : null;

    // True when the rpos/ stem is one of the bundled ship meshes (vs a non-ship asset or a CDN-only ship).
    public static bool IsBundledShip(string stem) => StemToEnum.ContainsKey(stem);
}
