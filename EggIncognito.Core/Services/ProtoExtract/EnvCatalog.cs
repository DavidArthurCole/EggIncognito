namespace EggIncognito.Services.ProtoExtract;

// The shipped farm-environment meshes (carved from the app bundle, .rpo) and the presets that group them
// into a scene backdrop for the playground. Pure data + naming: the controller does the file IO + decode.
// A "piece" is one env mesh stem (file name without extension under Assets/env). A "preset" is an ordered
// set of pieces composed at world origin to form a backdrop (ground + scenery, no per-piece offset).
public static class EnvCatalog
{
    public sealed record EnvPiece(string Stem, string Label);
    // A placed piece in a preset: the mesh stem + a world offset (the meshes are authored near origin at
    // their single-plot spot; habs are offset into a row of 4 plots).
    public sealed record PlacedPiece(string Stem, float[] Offset);
    public sealed record EnvPreset(string Id, string Label, IReadOnlyList<PlacedPiece> Pieces);

    // Every env mesh shipped under Assets/env. The stem is the .rpo file name; the controller validates a
    // requested stem against this allowlist so no arbitrary file is served.
    public static readonly IReadOnlyList<EnvPiece> Pieces = new[]
    {
        new EnvPiece("ei_farm_ground", "Farm ground"),
        new EnvPiece("ei_farm", "Farm paths"),
        new EnvPiece("ei_farm_hardscape", "Hardscape"),
        new EnvPiece("ei_farm_misc", "Ground detail"),
        new EnvPiece("ei_chicken_display_ground", "Display ground"),
        new EnvPiece("coop", "Coop"),
        new EnvPiece("ei_silo_0_large", "Silo"),
        new EnvPiece("ei_silo", "Silo (alt)"),
        new EnvPiece("ei_depot_3", "Depot"),
        new EnvPiece("ei_fuel_tank_2", "Fuel tank"),
        new EnvPiece("ei_farm_mailbox_full", "Mailbox"),
        new EnvPiece("hab_1k", "Coop (1k)"),
        new EnvPiece("hab_10k", "Shack (10k)"),
        new EnvPiece("hab_eggtopia", "Eggtopia"),
        new EnvPiece("hab_monolith", "Monolith"),
        new EnvPiece("hab_portal", "Portal"),
        new EnvPiece("hab_chicken_universe", "Chicken Universe"),
    };

    // The hab meshes, for the playground's standalone hab picker (load one hab without a full preset).
    public static readonly IReadOnlyList<EnvPiece> Habs = new[]
    {
        new EnvPiece("hab_1k", "Coop (1k)"),
        new EnvPiece("hab_10k", "Shack (10k)"),
        new EnvPiece("hab_eggtopia", "Eggtopia"),
        new EnvPiece("hab_monolith", "Monolith"),
        new EnvPiece("hab_portal", "Portal"),
        new EnvPiece("hab_chicken_universe", "Chicken Universe"),
    };

    private static PlacedPiece At(string stem, float x = 0, float y = 0, float z = 0) => new(stem, new[] { x, y, z });

    // Backdrop presets, smallest to fullest. The flat terrain pieces sit at origin; buildings sit at their
    // authored single-plot offset; the hab row is spread across 4 plots in X.
    public static readonly IReadOnlyList<EnvPreset> Presets = new[]
    {
        new EnvPreset("ground", "Ground only", new[] { At("ei_farm_ground") }),
        new EnvPreset("display", "Display pedestal", new[] { At("ei_chicken_display_ground") }),
        new EnvPreset("farm", "Farm", new[]
        {
            At("ei_farm_ground"), At("ei_farm"), At("coop"), At("ei_silo_0_large", 8), At("ei_farm_mailbox_full"),
        }),
        new EnvPreset("farm_full", "Full farm", new[]
        {
            At("ei_farm_ground"), At("ei_farm"), At("coop"), At("ei_silo_0_large", 8), At("ei_silo", 12),
            At("ei_depot_3", 6), At("ei_fuel_tank_2", -8), At("ei_farm_mailbox_full"),
        }),
        new EnvPreset("farm_habs", "Farm + hab row", new[]
        {
            At("ei_farm_ground"), At("ei_farm"), At("coop"), At("ei_silo_0_large", 8),
            At("hab_10k", -20), At("hab_10k", -7), At("hab_10k", 6), At("hab_10k", 19),
        }),
    };

    public static bool IsKnownPiece(string stem) =>
        Pieces.Any(p => string.Equals(p.Stem, stem, StringComparison.Ordinal));

    public static EnvPreset? PresetById(string id) =>
        Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}
