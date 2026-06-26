namespace EggIncognito.Services;

// Maps the game's internal ship mesh asset name (the rpos/ file stem, e.g. "ei_ship_chicken_one") to
// EggLedger's MissionInfo.Spaceship enum NAME (e.g. "ChickenOne"). EggLedger keys its .glb consumption by
// the enum name, so the asset pipeline must rename ei_ship_* -> <EnumName>.glb. The map is explicit (not
// derived) because the file stems do not normalize to the enum names: the four high-tier ships use afx
// shell assets with different stems, and the bundle ships use codenames (millenium != quintillion display).
//
// Verified against egginc 1.36 (iOS bundle rpos/ + binary afx_ship_* string table) on 2026-06-26.
//
// The 11 Spaceship enum members (value order = tier order):
//   0 ChickenOne 1 ChickenNine 2 ChickenHeavy 3 Bcr 4 MilleniumChicken 5 CorellihenCorvette
//   6 Galeggtica 7 Chickfiant 8 Voyegger 9 Henerprise 10 Atreggies
//
// Mesh source per ship:
//   0-5 + 10: bundled in the app at rpos/ei_ship_*.rpo (extractable now).
//   6-9 (Galeggtica, Chickfiant/Defihent, Voyegger, Henerprise): NOT bundled - afx shell assets fetched
//   from auxbrain.com/dlc/shells/ on demand. Their CDN ids are unresolved (TODO), so they are listed here
//   with a null source stem and excluded from the bundle-export set until the shell path lands.
public static class ShipNameMap
{
    // One ship's identity across the three name spaces. BundleStem is the rpos/ file stem when the mesh
    // ships in the app, or null when the mesh is a CDN-only shell. ShellAsset is the afx_ship_* name the
    // CDN fetch keys off (the shell-id resolver maps that -> dlc/shells url).
    public sealed record Ship(int Tier, string EnumName, string Display, string? BundleStem, string? ShellAsset);

    public static readonly IReadOnlyList<Ship> All =
    [
        new(0, "ChickenOne", "Chicken One", "ei_ship_chicken_one", "afx_ship_chicken_1"),
        new(1, "ChickenNine", "Chicken Nine", "ei_ship_chicken_nine", "afx_ship_chicken_9"),
        new(2, "ChickenHeavy", "Chicken Heavy", "ei_ship_chicken_heavy", "afx_ship_chicken_heavy"),
        new(3, "Bcr", "BCR", "ei_ship_bcr", "afx_ship_bcr"),
        new(4, "MilleniumChicken", "Quintillion Chicken", "ei_ship_millenium_chicken", "afx_ship_millenium_chicken"),
        new(5, "CorellihenCorvette", "Cornish-Hen Corvette", "ei_ship_corellihen_corvette", "afx_ship_corellihen_corvette"),
        // 6-9: CDN shells only, no bundled rpos/ mesh. BundleStem null until shell ids resolve.
        new(6, "Galeggtica", "Galeggtica", null, "afx_ship_galeggtica"),
        new(7, "Chickfiant", "Defihent", null, "afx_ship_defihent"),
        new(8, "Voyegger", "Voyegger", null, "afx_ship_voyegger"),
        new(9, "Henerprise", "Henerprise", null, "afx_ship_henerprise"),
        new(10, "Atreggies", "Atreggies Henliner", "ei_ship_atreggies_shuttle", "afx_ship_atreggies"),
    ];

    // Lookup by the rpos/ file stem (mesh key from RpoAssetExtractor). Returns the enum name, or null when
    // the stem is not a ship (rpos/ holds 327 assets; only these are ships - the rest are habs, pipes,
    // vehicles, trophies, and the rooster/egg_shuttle launch props, all excluded).
    private static readonly Dictionary<string, string> StemToEnum =
        All.Where(s => s.BundleStem is not null)
           .ToDictionary(s => s.BundleStem!, s => s.EnumName, StringComparer.OrdinalIgnoreCase);

    public static string? EnumNameForStem(string stem) =>
        StemToEnum.TryGetValue(stem, out var e) ? e : null;

    // True when the rpos/ stem is one of the bundled ship meshes (vs a non-ship asset or a CDN-only ship).
    public static bool IsBundledShip(string stem) => StemToEnum.ContainsKey(stem);
}
