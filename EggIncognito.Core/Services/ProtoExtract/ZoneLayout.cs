namespace EggIncognito.Services.ProtoExtract;

// Fixed zone grid for the farm's buildable core, replacing the row-packing stopgap (FarmLayout.PackRow/
// CoreRows). Matches the user's reference layout: a horizontal path separates the BACK strip (habs + lab, not
// gravity-packed together) from everything below it. Below the path: the silo field (its own area, exempt from
// gravity/domino - never touched by repacking) sits left of ONE gravity-packed core row (Hoa, Hatchery, Mission
// Control, Fuel, left to right). The road runs below that row; the Depot sits alone across the road.
//
// Zone GRANULARITY is deliberately coarse: MidRow is a wide Z-band spanning the whole packed-core width, not a
// tight per-building box. Per-slot ordering within it comes from left-to-right packing by REAL mesh width
// (repackZoneRow in playground.js, called after every add) + the existing PlacementSolver.DominoNudge push, not
// from the zone rect itself.
//
// STOPGAP (CLAUDE.md "EXTRACT, don't author"): row Z bands + gap are hand-tuned, same convention as the row Z
// constants they replace. The real per-row bounds live in the game's FarmScene terrain layout, not yet
// disassembled. Silos (FarmLayout.SiloPos) and the hab row (FarmLayout.HabRowZ etc) already use EXTRACTED
// formulas and are wrapped as Fixed zones for addressability only, not replaced.
public static class ZoneLayout
{
    public enum ZoneId { Silos, Habs, Lab, MidRow, FrontRow }

    // AnchorX/AnchorZ = the zone's back-left corner. Width/Depth = the zone's extent.
    public sealed record Zone(ZoneId Id, float AnchorX, float AnchorZ, float Width, float Depth);

    // Row bands (Z).
    public const float MidRowZ = 5f; // Hoa/Hatchery/MissionControl/Fuel row, below the top path
    public const float FrontRowZ = 10f; // Depot row, across the road from MidRow
    public const float RowDepthBand = 6f; // generous Z-thickness of the mid row's drop band (covers any real tier depth)
    public const float ZoneGapX = 2.5f; // horizontal gap between adjacent buildings in a row
    public const float CoreLeftX = 2f; // left bound of the gravity-packed core row, right of the silo field
    public const float CoreWidth = 60f; // generous right bound so the row band covers the whole packed core

    public static readonly IReadOnlyDictionary<ZoneId, Zone> Zones = BuildZones();

    private static IReadOnlyDictionary<ZoneId, Zone> BuildZones()
    {
        // silo field: its OWN area, left of the core row, exempt from gravity/domino. FarmLayout.SiloPos already
        // places the silos there (negative-X); this rect is informational only.
        var silos = new Zone(ZoneId.Silos, AnchorX: -35f, AnchorZ: -2f, Width: 30f, Depth: 9f);
        // back strip, ABOVE the top path: habs + lab. Not gravity-packed with the core row below.
        var habs = new Zone(ZoneId.Habs, AnchorX: -35f, AnchorZ: FarmLayout.HabRowZ - 2f, Width: 70f, Depth: 4f);
        var lab = new Zone(ZoneId.Lab, AnchorX: 24f, AnchorZ: FarmLayout.HabRowZ - 2f, Width: 12f, Depth: 6f);

        var midRow = new Zone(ZoneId.MidRow, AnchorX: CoreLeftX, AnchorZ: MidRowZ, Width: CoreWidth, Depth: RowDepthBand);
        var frontRow = new Zone(ZoneId.FrontRow, AnchorX: CoreLeftX, AnchorZ: FrontRowZ, Width: CoreWidth, Depth: RowDepthBand);

        return new Dictionary<ZoneId, Zone>
        {
            [ZoneId.Silos] = silos, [ZoneId.Habs] = habs, [ZoneId.Lab] = lab,
            [ZoneId.MidRow] = midRow, [ZoneId.FrontRow] = frontRow,
        };
    }

    // Places Lab at its own back-strip zone (fixed, not gravity-packed); Hoa/Hatchery/MissionControl/Fuel at the
    // MidRow anchor (initial guess; repackZoneRow corrects X to real mesh width right after the batch add);
    // Depot at FrontRow. Recenter=true so the layout position is authoritative. All 5 core buildings render
    // left-pinned (recenterX="min" in playground.js's addGroup), so Pos = the zone's anchor corner, not center.
    public static IReadOnlyList<FarmLayout.Placed> Resolve(string lab, string hoa, string hatchery,
        string missionControl, string fuel, string depot)
    {
        FarmLayout.Placed At(ZoneId zone, string stem) => new(stem, [Zones[zone].AnchorX, 0f, Zones[zone].AnchorZ], 0, Recenter: true);

        return
        [
            At(ZoneId.Lab, lab),
            At(ZoneId.MidRow, hoa),
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
