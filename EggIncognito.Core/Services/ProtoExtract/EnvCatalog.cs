namespace EggIncognito.Services.ProtoExtract;

// The shipped farm-environment meshes (carved from the app bundle, .rpo) and the presets that group them
// into a scene backdrop for the playground. Pure data + naming: the controller does the file IO + decode.
// A "piece" is one env mesh stem (file name without extension under Assets/env). A "preset" is an ordered
// set of pieces composed at world origin to form a backdrop (ground + scenery, no per-piece offset).
public static class EnvCatalog
{
    public sealed record EnvPiece(string Stem, string Label);
    public sealed record EnvPreset(string Id, string Label, IReadOnlyList<string> Pieces);

    // Every env mesh shipped under Assets/env. The stem is the .rpo file name; the controller validates a
    // requested stem against this allowlist so no arbitrary file is served.
    public static readonly IReadOnlyList<EnvPiece> Pieces = new[]
    {
        new EnvPiece("ei_farm_ground", "Farm ground"),
        new EnvPiece("ei_farm", "Farm base"),
        new EnvPiece("ei_farm_hardscape", "Hardscape"),
        new EnvPiece("ei_farm_misc", "Scenery"),
        new EnvPiece("ei_chicken_display_ground", "Display ground"),
        new EnvPiece("coop", "Coop"),
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

    // Backdrop presets, smallest to fullest. Pieces compose at world origin in listed order.
    public static readonly IReadOnlyList<EnvPreset> Presets = new[]
    {
        new EnvPreset("ground", "Ground only", new[] { "ei_farm_ground" }),
        new EnvPreset("display", "Display pedestal", new[] { "ei_chicken_display_ground" }),
        new EnvPreset("farm", "Farm", new[] { "ei_farm_ground", "ei_farm", "ei_farm_hardscape" }),
        new EnvPreset("farm_full", "Full farm", new[] { "ei_farm_ground", "ei_farm", "ei_farm_hardscape", "ei_farm_misc" }),
        new EnvPreset("farm_habs", "Farm + habs", new[] { "ei_farm_ground", "ei_farm", "ei_farm_hardscape", "hab_1k", "hab_10k" }),
    };

    public static bool IsKnownPiece(string stem) =>
        Pieces.Any(p => string.Equals(p.Stem, stem, StringComparison.Ordinal));

    public static EnvPreset? PresetById(string id) =>
        Presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
}
