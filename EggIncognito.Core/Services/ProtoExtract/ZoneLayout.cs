namespace EggIncognito.Services.ProtoExtract;

// Fixed zone grid for the farm's buildable core, replacing the row-packing stopgap (FarmLayout.PackRow/
// CoreRows). Matches the user's reference layout: silo field + hab row on the left/back, then Lab/Hoa (back
// row), Hatchery/Mission Control/Fuel (mid row), Depot (front row), bounded by the top path and the road.
//
// Zone GRANULARITY is deliberately coarse: a zone is a wide Z-band (back/mid/front row) spanning the whole
// buildable core width, not a tight per-building box. Per-slot ordering within a row comes from left-to-right
// packing by REAL mesh width (repackZoneRow in playground.js, called after every add) + the existing
// PlacementSolver.DominoNudge push, not from the zone rect itself. A tight per-building rect would need to be
// re-synced after every repack/tier-swap to stay accurate; a row band never goes stale.
//
// STOPGAP (CLAUDE.md "EXTRACT, don't author"): row Z bands + gap are hand-tuned, same convention as the row Z
// constants they replace. The real per-row bounds live in the game's FarmScene terrain layout, not yet
// disassembled. Silos (FarmLayout.SiloPos) and the hab row (FarmLayout.HabRowZ etc) already use EXTRACTED
// formulas and are wrapped as Fixed zones for addressability only, not replaced.
public static class ZoneLayout
{
    public enum ZoneId { Silos, Habs, BackRow, MidRow, FrontRow }

    // AnchorX/AnchorZ = the zone's back-left corner. Width/Depth = the zone's extent. A row zone spans the
    // whole buildable core width (not a single building's box); Silos/Habs keep their own reserved rect.
    public sealed record Zone(ZoneId Id, float AnchorX, float AnchorZ, float Width, float Depth);

    // Row bands (Z), matching the existing extracted/tuned constants they replace.
    public const float BackRowZ = -4f; // was FarmLayout.RowBackZ / Playground.razor's BackRowBackZ
    public const float MidRowZ = 5f; // was FarmLayout.RowMidZ
    public const float FrontRowZ = 10f; // was FarmLayout.RowFrontZ
    public const float RowDepthBand = 6f; // generous Z-thickness of each row's drop band (covers any real tier depth)
    public const float ZoneGapX = 2.5f; // horizontal gap between adjacent buildings in a row
    public const float CoreLeftX = 2f; // left bound of the buildable core, past the silo field's path
    public const float CoreWidth = 60f; // generous right bound so the row band covers the whole packed core

    public static readonly IReadOnlyDictionary<ZoneId, Zone> Zones = BuildZones();

    private static IReadOnlyDictionary<ZoneId, Zone> BuildZones()
    {
        // silo field: negative-X (FarmLayout.SiloPos already places it there). Reserved rect is informational.
        var silos = new Zone(ZoneId.Silos, AnchorX: -35f, AnchorZ: -2f, Width: 30f, Depth: 9f);
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

    // Places each core building at its row's back-left ANCHOR (initial guess; repackZoneRow corrects X to the
    // real mesh width immediately after the batch add). Recenter=true so the layout position is authoritative.
    // All 6 core buildings render left-pinned (recenterX="min" in playground.js's addGroup), so Pos = the row's
    // anchor corner, not its center.
    public static IReadOnlyList<FarmLayout.Placed> Resolve(string lab, string hoa, string hatchery,
        string missionControl, string fuel, string depot)
    {
        FarmLayout.Placed At(ZoneId row, string stem) => new(stem, [Zones[row].AnchorX, 0f, Zones[row].AnchorZ], 0, Recenter: true);

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
