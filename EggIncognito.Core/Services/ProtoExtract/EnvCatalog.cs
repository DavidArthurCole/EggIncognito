namespace EggIncognito.Services.ProtoExtract;

// The farm-environment mesh catalog (names only, no asset bytes): the buildings, habs, and ground pieces a
// designer can place. The meshes themselves are pulled off a device + cached (DeviceMeshProvider); this is
// just the allowlist of known stems + their display labels. Layouts are authored in the designer (EnvDesign),
// not hardcoded here.
//
// Most building meshes are authored at their real in-game plot position in their own vertex coords (depot
// z~7-12, hyperloop z~19-27, lab x~4-10, ...), so placing them at world origin self-positions them. Only the
// repeated rows (habs, silos) are centered-at-origin single plots that the layout instances at multiple spots.
public static class EnvCatalog
{
    // Singleton = the scene holds at most one (terrain layers + the self-placing single-slot buildings).
    // Group = the picker section. Family = the swap group: a placed element can be switched to another piece
    // in the same family (a hab tier for a hab, a lab level for a lab); "" = not swappable.
    public sealed record EnvPiece(string Stem, string Label, string Group, bool Singleton = false, string Family = "");

    public static readonly IReadOnlyList<EnvPiece> Pieces = new[]
    {
        new EnvPiece("ei_farm_ground", "Farm ground", "Terrain", Singleton: true),
        new EnvPiece("ei_farm", "Farm paths", "Terrain", Singleton: true),
        new EnvPiece("ei_farm_hardscape", "Hardscape", "Terrain", Singleton: true),
        new EnvPiece("ei_farm_misc", "Ground detail", "Terrain", Singleton: true),
        new EnvPiece("ei_chicken_display_ground", "Display ground", "Terrain", Singleton: true),

        new EnvPiece("hab_1k", "Coop (1k)", "Habs", Family: "hab"),
        new EnvPiece("hab_10k", "Shack (10k)", "Habs", Family: "hab"),
        new EnvPiece("hab_eggtopia", "Eggtopia", "Habs", Family: "hab"),
        new EnvPiece("hab_monolith", "Monolith", "Habs", Family: "hab"),
        new EnvPiece("hab_portal", "Portal", "Habs", Family: "hab"),
        new EnvPiece("hab_chicken_universe", "Chicken Universe", "Habs", Family: "hab"),

        new EnvPiece("ei_silo_0_large", "Silo", "Storage"),
        new EnvPiece("ei_silo", "Silo (alt)", "Storage"),
        new EnvPiece("ei_depot_1", "Depot (1)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_depot_2", "Depot (2)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_depot_3", "Depot (3)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_depot_4", "Depot (4)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_depot_5", "Depot (5)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_depot_6", "Depot (6)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_depot_7", "Depot (7)", "Storage", Singleton: true, Family: "depot"),
        new EnvPiece("ei_fuel_tank_1", "Fuel tank (1)", "Storage", Singleton: true, Family: "fuel"),
        new EnvPiece("ei_fuel_tank_2", "Fuel tank (2)", "Storage", Singleton: true, Family: "fuel"),
        new EnvPiece("ei_fuel_tank_3", "Fuel tank (3)", "Storage", Singleton: true, Family: "fuel"),
        new EnvPiece("ei_fuel_tank_4", "Fuel tank (4)", "Storage", Singleton: true, Family: "fuel"),
        new EnvPiece("ei_hyperloop_stop", "Hyperloop station", "Storage", Singleton: true),
        new EnvPiece("ei_hyperloop_track", "Hyperloop track", "Storage", Singleton: true),

        new EnvPiece("ei_lab_1", "Research lab (1)", "Buildings", Singleton: true, Family: "lab"),
        new EnvPiece("ei_lab_2", "Research lab (2)", "Buildings", Singleton: true, Family: "lab"),
        new EnvPiece("ei_lab_3", "Research lab (3)", "Buildings", Singleton: true, Family: "lab"),
        new EnvPiece("ei_lab_4", "Research lab (4)", "Buildings", Singleton: true, Family: "lab"),
        new EnvPiece("ei_lab_5", "Research lab (5)", "Buildings", Singleton: true, Family: "lab"),
        new EnvPiece("ei_lab_6", "Research lab (6)", "Buildings", Singleton: true, Family: "lab"),
        new EnvPiece("ei_mission_control_1", "Mission control (1)", "Buildings", Singleton: true, Family: "mission"),
        new EnvPiece("ei_mission_control_2", "Mission control (2)", "Buildings", Singleton: true, Family: "mission"),
        new EnvPiece("ei_mission_control_3", "Mission control (3)", "Buildings", Singleton: true, Family: "mission"),
        new EnvPiece("ei_hoa_1", "HOA (1)", "Buildings", Singleton: true, Family: "hoa"),
        new EnvPiece("ei_hoa_2", "HOA (2)", "Buildings", Singleton: true, Family: "hoa"),
        new EnvPiece("ei_hoa_3", "HOA (3)", "Buildings", Singleton: true, Family: "hoa"),
        new EnvPiece("ei_trophy_case", "Trophy case", "Buildings", Singleton: true, Family: "trophy"),
        new EnvPiece("ei_trophy_case2", "Trophy case (2)", "Buildings", Singleton: true, Family: "trophy"),
        new EnvPiece("ei_afx_construction_site", "Artifact hall", "Buildings", Singleton: true),

        // The egg hatchery (between the depot + research lab). One per farm; the variation dropdown swaps the
        // egg type. Self-places (the mesh sits at the real plot). Sub-part meshes (_top/_ring/_orb/...) are
        // excluded; they are pieces of specific hatcheries, not standalone.
        new EnvPiece("ei_hatchery_edible", "Hatchery (Edible)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_superfood", "Hatchery (Superfood)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_medical", "Hatchery (Medical)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_supermaterial", "Hatchery (Super Material)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_fusion", "Hatchery (Fusion)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_quantum", "Hatchery (Quantum)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_immortality", "Hatchery (Immortality)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_tachyon", "Hatchery (Tachyon)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_graviton", "Hatchery (Graviton)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_dilithium", "Hatchery (Dilithium)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_prodigy", "Hatchery (Prodigy)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_terraform", "Hatchery (Terraform)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_antimatter", "Hatchery (Antimatter)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_darkmatter", "Hatchery (Dark Matter)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_ai", "Hatchery (AI)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_vision", "Hatchery (Nebula)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_universe", "Hatchery (Universe)", "Buildings", Singleton: true, Family: "hatchery"),
        new EnvPiece("ei_hatchery_enlightenment", "Hatchery (Enlightenment)", "Buildings", Singleton: true, Family: "hatchery"),

        new EnvPiece("coop", "Coop", "Structures", Singleton: true),
        new EnvPiece("ei_farm_mailbox_full", "Mailbox", "Structures", Singleton: true),
    };

    // The hab meshes (the "Habs" group), for any caller wanting just the habs.
    public static IReadOnlyList<EnvPiece> Habs =>
        Pieces.Where(p => p.Group == "Habs").ToList();

    // The other pieces in a piece's swap family (a hab tier's siblings, a lab level's siblings). Empty for
    // pieces with no family. Used to offer a "switch variation" dropdown on a placed element.
    public static IReadOnlyList<EnvPiece> Family(string stem)
    {
        var fam = Pieces.FirstOrDefault(p => p.Stem == stem)?.Family ?? "";
        if (string.IsNullOrEmpty(fam)) return [];
        return Pieces.Where(p => p.Family == fam).ToList();
    }

    public static bool IsKnownPiece(string stem) =>
        Pieces.Any(p => string.Equals(p.Stem, stem, StringComparison.Ordinal));

    // Maps an env stem to its ShellSpec.AssetType enum name (the C# PascalCase form), so a placed element can be
    // matched to the shell-set member that reskins that asset type. Returns null for terrain + pieces with no
    // shellable asset type. A table, not reflection: stem naming and enum names do not align 1:1 (hab_1k ->
    // Hab1k, hab_10k -> Hab10K, hab_eggtopia -> Eggtopia).
    private static readonly Dictionary<string, string?> _assetTypeByStem = new(StringComparer.Ordinal)
    {
        ["hab_1k"] = "Hab1K", ["hab_10k"] = "Hab10K", ["hab_eggtopia"] = "Eggtopia",
        ["hab_monolith"] = "Monolith", ["hab_portal"] = "PlanetPortal", ["hab_chicken_universe"] = "ChickenUniverse",
        ["ei_depot_1"] = "Depot1", ["ei_depot_2"] = "Depot2", ["ei_depot_3"] = "Depot3", ["ei_depot_4"] = "Depot4",
        ["ei_depot_5"] = "Depot5", ["ei_depot_6"] = "Depot6", ["ei_depot_7"] = "Depot7",
        ["ei_fuel_tank_1"] = "FuelTank1", ["ei_fuel_tank_2"] = "FuelTank2",
        ["ei_fuel_tank_3"] = "FuelTank3", ["ei_fuel_tank_4"] = "FuelTank4",
        ["ei_lab_1"] = "Lab1", ["ei_lab_2"] = "Lab2", ["ei_lab_3"] = "Lab3",
        ["ei_lab_4"] = "Lab4", ["ei_lab_5"] = "Lab5", ["ei_lab_6"] = "Lab6",
        ["ei_mission_control_1"] = "MissionControl1", ["ei_mission_control_2"] = "MissionControl2",
        ["ei_mission_control_3"] = "MissionControl3",
        ["ei_hoa_1"] = "Hoa1", ["ei_hoa_2"] = "Hoa2", ["ei_hoa_3"] = "Hoa3",
        ["ei_trophy_case"] = "TrophyCase", ["ei_trophy_case2"] = "TrophyCase",
        ["ei_silo_0_large"] = "Silo0Large", ["ei_silo"] = "Silo0Large",
        ["ei_farm_mailbox_full"] = "Mailbox", ["ei_farm_hardscape"] = "Hardscape",
        ["ei_hyperloop_stop"] = "Hyperloop", ["ei_hyperloop_track"] = "Hyperloop",
    };

    public static string? AssetTypeOf(string stem)
    {
        if (stem.StartsWith("ei_hatchery_", StringComparison.Ordinal))
        {
            var suffix = stem["ei_hatchery_".Length..];
            return suffix switch
            {
                "edible" => "HatcheryEdible", "superfood" => "HatcherySuperfood", "medical" => "HatcheryMedical",
                "supermaterial" => "HatcherySupermaterial", "fusion" => "HatcheryFusion", "quantum" => "HatcheryQuantum",
                "immortality" => "HatcheryImmortality", "tachyon" => "HatcheryTachyon", "graviton" => "HatcheryGraviton",
                "dilithium" => "HatcheryDilithium", "prodigy" => "HatcheryProdigy", "terraform" => "HatcheryTerraform",
                "antimatter" => "HatcheryAntimatter", "darkmatter" => "HatcheryDarkMatter", "ai" => "HatcheryAi",
                "vision" => "HatcheryNebula", "universe" => "HatcheryUniverse", "enlightenment" => "HatcheryEnlightenment",
                _ => null,
            };
        }
        return _assetTypeByStem.TryGetValue(stem, out var t) ? t : null;
    }
}
