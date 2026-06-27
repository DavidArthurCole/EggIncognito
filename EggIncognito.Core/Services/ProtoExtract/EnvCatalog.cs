namespace EggIncognito.Services.ProtoExtract;

// The farm-environment mesh catalog (names only, no asset bytes): the buildings, habs, and ground pieces a
// designer can place. The meshes themselves are pulled off a device + cached (DeviceMeshProvider); this is
// just the allowlist of known stems + their display labels. Layouts are authored in the designer (EnvDesign),
// not hardcoded here.
public static class EnvCatalog
{
    public sealed record EnvPiece(string Stem, string Label);

    // Every placeable env mesh stem + label. A requested stem is validated against this allowlist so no
    // arbitrary file is served off a device.
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

    // The hab meshes, grouped for the picker's hab section.
    public static readonly IReadOnlyList<EnvPiece> Habs = new[]
    {
        new EnvPiece("hab_1k", "Coop (1k)"),
        new EnvPiece("hab_10k", "Shack (10k)"),
        new EnvPiece("hab_eggtopia", "Eggtopia"),
        new EnvPiece("hab_monolith", "Monolith"),
        new EnvPiece("hab_portal", "Portal"),
        new EnvPiece("hab_chicken_universe", "Chicken Universe"),
    };

    public static bool IsKnownPiece(string stem) =>
        Pieces.Any(p => string.Equals(p.Stem, stem, StringComparison.Ordinal));
}
