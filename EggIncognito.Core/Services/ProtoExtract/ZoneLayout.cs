namespace EggIncognito.Services.ProtoExtract;

// Fixed zone grid for the farm's buildable core. A horizontal path separates the hab row (above) from the core
// area (below). Below the path, left to right: the silo field (its own area, exempt from gravity/domino), then
// 3 gravity-packed rows: BackRow (Research Lab, HOA), MidRow (Chicken Run Outflow, Hatchery, Mission Control,
// Fuel Tank), FrontRow (Depot alone, across the road). Zone granularity is deliberately coarse, a wide Z-band
// per row; per-slot ordering comes from left-to-right packing by real mesh width (repackZoneRow in
// playground.js) plus PlacementSolver.DominoNudge.
//
// STOPGAP (CLAUDE.md "EXTRACT, don't author"): row Z bands + gap are hand-tuned. The real per-row bounds live
// in the game's FarmScene terrain layout, not yet disassembled. Silos and the hab row already use extracted
// formulas and are wrapped as Fixed zones for addressability only.
public static class ZoneLayout
{
    public enum ZoneId { Silos, Habs, BackRow, MidRow, FrontRow }

    // AnchorX/AnchorZ = the zone's back-left corner. Width/Depth = the zone's extent.
    public sealed record Zone(ZoneId Id, float AnchorX, float AnchorZ, float Width, float Depth);

    // Row bands (Z).
    public const float BackRowZ = -4f; // Lab/HOA row, below the top hab path
    public const float MidRowZ = 5f; // ChickenOutflow/Hatchery/MissionControl/Fuel row
    public const float FrontRowZ = 10f; // Depot row, across the road
    public const float RowDepthBand = 6f; // generous Z-thickness of a row's drop band (covers any real tier depth)
    public const float ZoneGapX = 2.5f; // horizontal gap between adjacent buildings in a row
    public const float CoreLeftX = 2f; // left bound of the gravity-packed core rows, right of the silo field
    public const float CoreWidth = 60f; // generous right bound so a row band covers the whole packed core

    public static readonly IReadOnlyDictionary<ZoneId, Zone> Zones = BuildZones();

    private static IReadOnlyDictionary<ZoneId, Zone> BuildZones()
    {
        // silo field: its OWN area, left of the core rows, exempt from gravity/domino. FarmLayout.SiloPos
        // already places the silos there (negative-X); this rect is informational only.
        var silos = new Zone(ZoneId.Silos, AnchorX: -35f, AnchorZ: -2f, Width: 30f, Depth: 9f);
        // hab row, ABOVE the top path. Not gravity-packed with the core rows below.
        var habs = new Zone(ZoneId.Habs, AnchorX: -35f, AnchorZ: FarmLayout.HabRowZ - 2f, Width: 70f, Depth: 4f);

        var backRow = new Zone(ZoneId.BackRow, AnchorX: CoreLeftX, AnchorZ: BackRowZ, Width: CoreWidth, Depth: RowDepthBand);
        var midRow = new Zone(ZoneId.MidRow, AnchorX: CoreLeftX, AnchorZ: MidRowZ, Width: CoreWidth, Depth: RowDepthBand);
        var frontRow = new Zone(ZoneId.FrontRow, AnchorX: CoreLeftX, AnchorZ: FrontRowZ, Width: CoreWidth, Depth: RowDepthBand);

        return new Dictionary<ZoneId, Zone>
        {
            [ZoneId.Silos] = silos, [ZoneId.Habs] = habs,
            [ZoneId.BackRow] = backRow, [ZoneId.MidRow] = midRow, [ZoneId.FrontRow] = frontRow,
        };
    }

    // Places Lab+Hoa at BackRow, Hatchery/MissionControl/Fuel at MidRow, Depot at FrontRow. All 6 core buildings
    // render left-pinned, so Pos is the zone's anchor corner, not center.
    public static IReadOnlyList<FarmLayout.Placed> Resolve(string lab, string hoa, string hatchery,
        string missionControl, string fuel, string depot)
    {
        FarmLayout.Placed At(ZoneId zone, string stem) => new(stem, [Zones[zone].AnchorX, 0f, Zones[zone].AnchorZ], 0, Recenter: true);

        return
        [
            At(ZoneId.BackRow, lab),
            At(ZoneId.BackRow, hoa),
            At(ZoneId.MidRow, hatchery),
            At(ZoneId.MidRow, missionControl),
            At(ZoneId.MidRow, fuel),
            At(ZoneId.FrontRow, depot),
        ];
    }

    // Whether (x, z) lands inside any zone's rect.
    public static bool IsInsideAnyZone(float x, float z)
    {
        foreach (var zone in Zones.Values)
        {
            if (x >= zone.AnchorX && x <= zone.AnchorX + zone.Width && z >= zone.AnchorZ && z <= zone.AnchorZ + zone.Depth)
                return true;
        }
        return false;
    }
}
