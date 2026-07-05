namespace EggIncognito.Services.ProtoExtract;

// Fixed 2D zone grid for the farm's buildable core, replacing the row-packing stopgap (FarmLayout.PackRow/
// CoreRows). Each zone is an addressable slot with a fixed resting anchor + a reserved footprint, matching the
// user's reference layout: silo field + hab row on the left/back, then Lab/Hoa (back row), Chicken Run Outflow/
// Hatchery/Mission Control/Fuel (mid row), Depot (front row), bounded by the top path and the road.
//
// STOPGAP (CLAUDE.md "EXTRACT, don't author"): zone anchors/sizes below are hand-tuned, same convention as the
// row Z constants they replace. The real per-zone bounds live in the game's FarmScene terrain layout, not yet
// disassembled. Silos (FarmLayout.SiloPos) and the hab row (FarmLayout.HabRowZ etc) already use EXTRACTED
// formulas and are wrapped as Fixed-content zones for addressability only, not replaced.
public static class ZoneLayout
{
    public enum ZoneId { Silos, Habs, Lab, Hoa, ChickenOutflow, Hatchery, MissionControl, Fuel, Depot }

    public enum ZoneContent { Fixed, Single }

    // AnchorX/AnchorZ = the zone's back-left corner (local origin), so a zone's own coordinates start at ~0,0
    // as the user specified for the depot. Width/Depth = the reserved footprint (building + grow margin).
    public sealed record Zone(ZoneId Id, ZoneContent Content, float AnchorX, float AnchorZ, float Width, float Depth);

    // Row bands (Z), matching the existing extracted/tuned constants they replace.
    public const float BackRowZ = -4f; // was FarmLayout.RowBackZ / Playground.razor's BackRowBackZ
    public const float MidRowZ = 5f; // was FarmLayout.RowMidZ
    public const float FrontRowZ = 10f; // was FarmLayout.RowFrontZ
    public const float ZoneGapX = 2.5f; // horizontal gap between adjacent zones in a row, same as RowGap
    public const float CoreLeftX = 2f; // left bound of the buildable core, past the silo field's path

    public static readonly IReadOnlyDictionary<ZoneId, Zone> Zones = BuildZones();

    private static IReadOnlyDictionary<ZoneId, Zone> BuildZones()
    {
        // silo field: rows 0-2, col 0, negative-X (FarmLayout.SiloPos already places it there). Reserved
        // footprint is informational only (Fixed content is not domino-pushed).
        var silos = new Zone(ZoneId.Silos, ZoneContent.Fixed, AnchorX: -20f, AnchorZ: -1f, Width: 14f, Depth: 7f);
        var habs = new Zone(ZoneId.Habs, ZoneContent.Fixed, AnchorX: -20f, AnchorZ: FarmLayout.HabRowZ - 1f, Width: 40f, Depth: 3f);

        var lab = new Zone(ZoneId.Lab, ZoneContent.Single, AnchorX: CoreLeftX, AnchorZ: BackRowZ, Width: 9f, Depth: 4f);
        var hoa = new Zone(ZoneId.Hoa, ZoneContent.Single, AnchorX: lab.AnchorX + lab.Width + ZoneGapX, AnchorZ: BackRowZ, Width: 8f, Depth: 4f);

        var chickenOutflow = new Zone(ZoneId.ChickenOutflow, ZoneContent.Single, AnchorX: CoreLeftX, AnchorZ: MidRowZ, Width: 3f, Depth: 3f);
        var hatchery = new Zone(ZoneId.Hatchery, ZoneContent.Single, AnchorX: chickenOutflow.AnchorX + chickenOutflow.Width + ZoneGapX, AnchorZ: MidRowZ, Width: 8f, Depth: 5f);
        var missionControl = new Zone(ZoneId.MissionControl, ZoneContent.Single, AnchorX: hatchery.AnchorX + hatchery.Width + ZoneGapX, AnchorZ: MidRowZ, Width: 9f, Depth: 5f);
        var fuel = new Zone(ZoneId.Fuel, ZoneContent.Single, AnchorX: missionControl.AnchorX + missionControl.Width + ZoneGapX, AnchorZ: MidRowZ, Width: 5f, Depth: 5f);

        var depot = new Zone(ZoneId.Depot, ZoneContent.Single, AnchorX: CoreLeftX, AnchorZ: FrontRowZ, Width: 10f, Depth: 5f);

        return new Dictionary<ZoneId, Zone>
        {
            [ZoneId.Silos] = silos, [ZoneId.Habs] = habs,
            [ZoneId.Lab] = lab, [ZoneId.Hoa] = hoa,
            [ZoneId.ChickenOutflow] = chickenOutflow, [ZoneId.Hatchery] = hatchery,
            [ZoneId.MissionControl] = missionControl, [ZoneId.Fuel] = fuel,
            [ZoneId.Depot] = depot,
        };
    }

    // Places each Single-content zone's building at its zone's CENTER (anchor + half width/depth), Recenter=true
    // so the layout position is authoritative (matches PackRow's existing Recenter contract). This is the
    // auto-arrange INITIAL pass; RunDomino (Playground.razor) still handles post-swap growth pushing via the
    // existing PlacementSolver.DominoNudge path, unchanged by this method.
    public static IReadOnlyList<FarmLayout.Placed> Resolve(string lab, string hoa, string hatchery,
        string missionControl, string fuel, string depot)
    {
        FarmLayout.Placed At(ZoneId id, string stem)
        {
            var z = Zones[id];
            return new FarmLayout.Placed(stem, [z.AnchorX + z.Width / 2f, 0f, z.AnchorZ + z.Depth / 2f], 0, Recenter: true);
        }

        return
        [
            At(ZoneId.Lab, lab),
            At(ZoneId.Hoa, hoa),
            At(ZoneId.Hatchery, hatchery),
            At(ZoneId.MissionControl, missionControl),
            At(ZoneId.Fuel, fuel),
            At(ZoneId.Depot, depot),
        ];
    }

    // The zone (if any) whose current rect contains (x, z). `current` overrides a zone's reserved rect with its
    // post-domino-push live rect (wider tiers grow their zone); pass an empty dict to use the reserved bounds.
    public static bool IsInsideAnyZone(float x, float z, IReadOnlyDictionary<ZoneId, Zone>? current = null)
    {
        var zones = current ?? Zones;
        foreach (var zone in zones.Values)
        {
            if (x >= zone.AnchorX && x <= zone.AnchorX + zone.Width && z >= zone.AnchorZ && z <= zone.AnchorZ + zone.Depth)
                return true;
        }
        return false;
    }
}
