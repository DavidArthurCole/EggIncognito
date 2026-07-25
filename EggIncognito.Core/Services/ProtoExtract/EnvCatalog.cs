namespace EggIncognito.Services.ProtoExtract;

public static class EnvCatalog {
    public static readonly IReadOnlyList<EnvPiece> Pieces = [
        new("ei_farm_ground", "Farm ground", "Terrain", true),
        new("ei_farm", "Farm paths", "Terrain", true),
        new("ei_farm_hardscape", "Hardscape", "Terrain", true),
        new("ei_farm_misc", "Ground detail", "Terrain", true),
        new("ei_chicken_display_ground", "Display ground", "Terrain", true),


        new("coop", "Coop", "Habs", Family: "hab"),
        new("shack", "Shack", "Habs", Family: "hab"),
        new("super_shack", "Super Shack", "Habs", Family: "hab"),
        new("short_house", "Short House", "Habs", Family: "hab"),
        new("the_standard", "The Standard", "Habs", Family: "hab"),
        new("long_house", "Long House", "Habs", Family: "hab"),
        new("double_decker", "Double Decker", "Habs", Family: "hab"),
        new("warehouse", "Warehouse", "Habs", Family: "hab"),
        new("center", "Center", "Habs", Family: "hab"),
        new("bunker", "Bunker", "Habs", Family: "hab"),
        new("eggkea", "Eggkea", "Habs", Family: "hab"),
        new("hab_1k", "HAB 1000", "Habs", Family: "hab"),
        new("hanger", "Hangar", "Habs", Family: "hab"),
        new("tower", "Tower", "Habs", Family: "hab"),
        new("hab_10k", "HAB 10,000", "Habs", Family: "hab"),
        new("hab_eggtopia", "Eggtopia", "Habs", Family: "hab"),
        new("hab_monolith", "Monolith", "Habs", Family: "hab"),
        new("hab_portal", "Planet Portal", "Habs", Family: "hab"),
        new("hab_chicken_universe", "Chicken Universe", "Habs", Family: "hab"),

        new("ei_silo_0_large", "Silo", "Storage", Family: "silo"),
        new("ei_silo", "Silo (alt)", "Storage", Family: "silo"),
        new("ei_depot_1", "Depot (1)", "Storage", true, "depot"),
        new("ei_depot_2", "Depot (2)", "Storage", true, "depot"),
        new("ei_depot_3", "Depot (3)", "Storage", true, "depot"),
        new("ei_depot_4", "Depot (4)", "Storage", true, "depot"),
        new("ei_depot_5", "Depot (5)", "Storage", true, "depot"),
        new("ei_depot_6", "Depot (6)", "Storage", true, "depot"),
        new("ei_depot_7", "Depot (7)", "Storage", true, "depot"),
        new("ei_fuel_tank_1", "Fuel tank (1)", "Storage", true, "fuel"),
        new("ei_fuel_tank_2", "Fuel tank (2)", "Storage", true, "fuel"),
        new("ei_fuel_tank_3", "Fuel tank (3)", "Storage", true, "fuel"),
        new("ei_fuel_tank_4", "Fuel tank (4)", "Storage", true, "fuel"),
        new("ei_hyperloop_stop", "Hyperloop station", "Storage", true),
        new("ei_hyperloop_track", "Hyperloop track", "Storage", true),

        new("ei_lab_1", "Research lab (1)", "Buildings", true, "lab"),
        new("ei_lab_2", "Research lab (2)", "Buildings", true, "lab"),
        new("ei_lab_3", "Research lab (3)", "Buildings", true, "lab"),
        new("ei_lab_4", "Research lab (4)", "Buildings", true, "lab"),
        new("ei_lab_5", "Research lab (5)", "Buildings", true, "lab"),
        new("ei_lab_6", "Research lab (6)", "Buildings", true, "lab"),
        new("ei_mission_control_1", "Mission control (1)", "Buildings", true, "mission"),
        new("ei_mission_control_2", "Mission control (2)", "Buildings", true, "mission"),
        new("ei_mission_control_3", "Mission control (3)", "Buildings", true, "mission"),
        new("ei_hoa_1", "Artifact hall (1)", "Buildings", true, "hoa"),
        new("ei_hoa_2", "Artifact hall (2)", "Buildings", true, "hoa"),
        new("ei_hoa_3", "Artifact hall (3)", "Buildings", true, "hoa"),
        new("ei_trophy_case", "Trophy case", "Buildings", true, "trophy"),
        new("ei_trophy_case2", "Trophy case (2)", "Buildings", true, "trophy"),

        new("ei_afx_construction_site", "Artifact hall (construction)", "Buildings", true, "hoa"),


        new("ei_hatchery_edible", "Hatchery (Edible)", "Buildings", true, "hatchery"),
        new("ei_hatchery_superfood", "Hatchery (Superfood)", "Buildings", true, "hatchery"),
        new("ei_hatchery_medical", "Hatchery (Medical)", "Buildings", true, "hatchery"),
        new("ei_hatchery_supermaterial", "Hatchery (Super Material)", "Buildings", true, "hatchery"),
        new("ei_hatchery_fusion", "Hatchery (Fusion)", "Buildings", true, "hatchery"),
        new("ei_hatchery_quantum", "Hatchery (Quantum)", "Buildings", true, "hatchery"),
        new("ei_hatchery_immortality", "Hatchery (Immortality)", "Buildings", true, "hatchery"),
        new("ei_hatchery_tachyon", "Hatchery (Tachyon)", "Buildings", true, "hatchery"),
        new("ei_hatchery_graviton", "Hatchery (Graviton)", "Buildings", true, "hatchery"),
        new("ei_hatchery_dilithium", "Hatchery (Dilithium)", "Buildings", true, "hatchery"),
        new("ei_hatchery_prodigy", "Hatchery (Prodigy)", "Buildings", true, "hatchery"),
        new("ei_hatchery_terraform", "Hatchery (Terraform)", "Buildings", true, "hatchery"),
        new("ei_hatchery_antimatter", "Hatchery (Antimatter)", "Buildings", true, "hatchery"),
        new("ei_hatchery_darkmatter", "Hatchery (Dark Matter)", "Buildings", true, "hatchery"),
        new("ei_hatchery_ai", "Hatchery (AI)", "Buildings", true, "hatchery"),
        new("ei_hatchery_vision", "Hatchery (Nebula)", "Buildings", true, "hatchery"),
        new("ei_hatchery_universe", "Hatchery (Universe)", "Buildings", true, "hatchery"),
        new("ei_hatchery_enlightenment", "Hatchery (Enlightenment)", "Buildings", true, "hatchery"),

        new("ei_farm_mailbox_full", "Mailbox", "Structures", true),


        new("ei_vehicle_semi", "Semi", "Vehicles"),
        new("ei_vehicle_pickup", "Pickup", "Vehicles"),
        new("ei_vehicle_trike", "Trike", "Vehicles"),
        new("ei_vehicle_transit_van", "Transit van", "Vehicles"),
        new("ei_vehicle_10ft", "10ft truck", "Vehicles"),
        new("ei_vehicle_24ft", "24ft truck", "Vehicles"),
        new("ei_vehicle_double_semi", "Double semi", "Vehicles"),
        new("ei_vehicle_future_semi", "Future semi", "Vehicles"),
        new("ei_vehicle_hover_semi", "Hover semi", "Vehicles"),
        new("ei_vehicle_mega_semi", "Mega semi", "Vehicles"),


        new("ei_ship_egg_shuttle", "Egg shuttle", "Ships"),
        new("ei_ship_rooster", "Rooster", "Ships"),
        new("ei_ship_bcr", "BCR", "Ships"),
        new("ei_ship_chicken_one", "Chicken One", "Ships"),
        new("ei_ship_chicken_nine", "Chicken Nine", "Ships"),
        new("ei_ship_chicken_heavy", "Chicken Heavy", "Ships"),
        new("ei_ship_corellihen_corvette", "Corellihen Corvette", "Ships"),
        new("ei_ship_millenium_chicken", "Millenium Chicken", "Ships"),
        new("ei_ship_atreggies_shuttle", "Atreggies Shuttle", "Ships")
    ];


    private static readonly Dictionary<string, string?> _assetTypeByStem = new(StringComparer.Ordinal) {
        ["coop"] = "Coop",
        ["shack"] = "Shack",
        ["super_shack"] = "SuperShack",
        ["short_house"] = "ShortHouse",
        ["the_standard"] = "TheStandard",
        ["long_house"] = "LongHouse",
        ["double_decker"] = "DoubleDecker",
        ["warehouse"] = "Warehouse",
        ["center"] = "Center",
        ["bunker"] = "Bunker",
        ["eggkea"] = "Eggkea",
        ["hab_1k"] = "Hab1K",
        ["hanger"] = "Hangar",
        ["tower"] = "Tower",
        ["hab_10k"] = "Hab10K",
        ["hab_eggtopia"] = "Eggtopia",
        ["hab_monolith"] = "Monolith",
        ["hab_portal"] = "PlanetPortal",
        ["hab_chicken_universe"] = "ChickenUniverse",
        ["ei_depot_1"] = "Depot1",
        ["ei_depot_2"] = "Depot2",
        ["ei_depot_3"] = "Depot3",
        ["ei_depot_4"] = "Depot4",
        ["ei_depot_5"] = "Depot5",
        ["ei_depot_6"] = "Depot6",
        ["ei_depot_7"] = "Depot7",
        ["ei_fuel_tank_1"] = "FuelTank1",
        ["ei_fuel_tank_2"] = "FuelTank2",
        ["ei_fuel_tank_3"] = "FuelTank3",
        ["ei_fuel_tank_4"] = "FuelTank4",
        ["ei_lab_1"] = "Lab1",
        ["ei_lab_2"] = "Lab2",
        ["ei_lab_3"] = "Lab3",
        ["ei_lab_4"] = "Lab4",
        ["ei_lab_5"] = "Lab5",
        ["ei_lab_6"] = "Lab6",
        ["ei_mission_control_1"] = "MissionControl1",
        ["ei_mission_control_2"] = "MissionControl2",
        ["ei_mission_control_3"] = "MissionControl3",
        ["ei_hoa_1"] = "Hoa1",
        ["ei_hoa_2"] = "Hoa2",
        ["ei_hoa_3"] = "Hoa3",
        ["ei_trophy_case"] = "TrophyCase",
        ["ei_trophy_case2"] = "TrophyCase",
        ["ei_silo_0_large"] = "Silo0Large",
        ["ei_silo"] = "Silo0Large",
        ["ei_farm_mailbox_full"] = "Mailbox",
        ["ei_farm_hardscape"] = "Hardscape",
        ["ei_hyperloop_stop"] = "Hyperloop",
        ["ei_hyperloop_track"] = "Hyperloop"
    };


    public static IReadOnlyList<EnvPiece> Habs =>
        Pieces.Where(p => p.Group == "Habs").ToList();


    public static IReadOnlyList<EnvPiece> Family(string stem) {
        string fam = Pieces.FirstOrDefault(p => p.Stem == stem)?.Family ?? "";
        return string.IsNullOrEmpty(fam) ? [] : [.. Pieces.Where(p => p.Family == fam)];
    }

    public static bool IsKnownPiece(string stem) =>
        Pieces.Any(p => string.Equals(p.Stem, stem, StringComparison.Ordinal));

    public static string? AssetTypeOf(string stem) {
        if (stem.StartsWith("ei_hatchery_", StringComparison.Ordinal)) {
            string suffix = stem["ei_hatchery_".Length..];
            return suffix switch {
                "edible" => "HatcheryEdible",
                "superfood" => "HatcherySuperfood",
                "medical" => "HatcheryMedical",
                "supermaterial" => "HatcherySupermaterial",
                "fusion" => "HatcheryFusion",
                "quantum" => "HatcheryQuantum",
                "immortality" => "HatcheryImmortality",
                "tachyon" => "HatcheryTachyon",
                "graviton" => "HatcheryGraviton",
                "dilithium" => "HatcheryDilithium",
                "prodigy" => "HatcheryProdigy",
                "terraform" => "HatcheryTerraform",
                "antimatter" => "HatcheryAntimatter",
                "darkmatter" => "HatcheryDarkMatter",
                "ai" => "HatcheryAi",
                "vision" => "HatcheryNebula",
                "universe" => "HatcheryUniverse",
                "enlightenment" => "HatcheryEnlightenment",
                _ => null
            };
        }

        return _assetTypeByStem.GetValueOrDefault(stem);
    }


    public sealed record EnvPiece(string Stem, string Label, string Group, bool Singleton = false, string Family = "");
}
