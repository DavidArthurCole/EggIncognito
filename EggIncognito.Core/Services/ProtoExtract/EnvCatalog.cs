namespace EggIncognito.Services.ProtoExtract;

// The farm-environment mesh catalog (names only, no asset bytes): the buildings, habs, and ground pieces a
// designer can place. The meshes themselves are pulled off a device + cached (DeviceMeshProvider); this is
// just the allowlist of known stems + their display labels. Layouts are authored in the designer (EnvDesign),
// not hardcoded here.
public static class EnvCatalog
{
    // Singleton = the scene holds at most one (the ground plane, paths, hardscape, ground detail, display
    // pedestal, mailbox). Adding a second is blocked client-side. Buildings/habs are repeatable.
    // Group = the picker section a piece appears under (Terrain / Habs / Storage / Structures).
    public sealed record EnvPiece(string Stem, string Label, string Group, bool Singleton = false);

    // Every placeable env mesh stem + label. A requested stem is validated against this allowlist so no
    // arbitrary file is served off a device.
    public static readonly IReadOnlyList<EnvPiece> Pieces = new[]
    {
        new EnvPiece("ei_farm_ground", "Farm ground", "Terrain", Singleton: true),
        new EnvPiece("ei_farm", "Farm paths", "Terrain", Singleton: true),
        new EnvPiece("ei_farm_hardscape", "Hardscape", "Terrain", Singleton: true),
        new EnvPiece("ei_farm_misc", "Ground detail", "Terrain", Singleton: true),
        new EnvPiece("ei_chicken_display_ground", "Display ground", "Terrain", Singleton: true),

        new EnvPiece("hab_1k", "Coop (1k)", "Habs"),
        new EnvPiece("hab_10k", "Shack (10k)", "Habs"),
        new EnvPiece("hab_eggtopia", "Eggtopia", "Habs"),
        new EnvPiece("hab_monolith", "Monolith", "Habs"),
        new EnvPiece("hab_portal", "Portal", "Habs"),
        new EnvPiece("hab_chicken_universe", "Chicken Universe", "Habs"),

        new EnvPiece("ei_silo_0_large", "Silo", "Storage"),
        new EnvPiece("ei_silo", "Silo (alt)", "Storage"),
        new EnvPiece("ei_depot_3", "Depot", "Storage"),
        new EnvPiece("ei_fuel_tank_2", "Fuel tank", "Storage"),

        new EnvPiece("coop", "Coop", "Structures"),
        new EnvPiece("ei_farm_mailbox_full", "Mailbox", "Structures", Singleton: true),
    };

    // The hab meshes (the "Habs" group), for any caller wanting just the habs.
    public static IReadOnlyList<EnvPiece> Habs =>
        Pieces.Where(p => p.Group == "Habs").ToList();

    public static bool IsKnownPiece(string stem) =>
        Pieces.Any(p => string.Equals(p.Stem, stem, StringComparison.Ordinal));
}
