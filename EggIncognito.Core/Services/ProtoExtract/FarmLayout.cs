using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Services.ProtoExtract;

// A game-like default farm layout: the standard set of farm elements at in-game plot positions, so the
// designer can one-click "Auto-arrange" a believable farm instead of freehand placing every piece. Most
// building meshes are authored at their real in-game plot position in their own vertex coords, so they
// self-place at world origin. Only repeated rows need index math: the silo formula is exact from disassembly
// (FarmScene::updateSilo); the hab row has no code spacing constant, so it uses a derived even spacing.
public static class FarmLayout
{
    // One placed element: the catalog stem + a world transform. Recenter = the renderer must recenter the mesh
    // on its origin so Pos is the sole placement authority (gravity-packed core buildings whose meshes carry a
    // baked plot offset). Self-placing pieces keep Recenter=false: their authored offset is the layout.
    public sealed record Placed(string Stem, float[] Pos, float RotY, float Scale = 1f, bool Recenter = false);

    // Hab row layout extracted from GameController::getHabPosition(int) (disasm). Each hab i sits at a fixed
    // back row Z, Y on the ground plane, and X = the running sum of earlier habs' widths plus HabGap, centered
    // by width*0.5. Width is each hab's own mesh bbox, so a wider hab pushes the rest right; the renderer does
    // the cumulative bbox-width walk since it holds the meshes.
    public const float HabRowZ = -10.5f; // getHabPosition ret[8]: row depth
    public const float HabRowY = 0f;
    public const float HabGap = 3f; // getHabPosition inter-hab gap constant
    public const float HabHalfStep = 0.5f; // getHabPosition centering coefficient
    private const float HabSpacing = 13f; // fallback uniform X step when a hab's mesh bbox is unavailable
    private const float HabZ = HabRowZ;
    private const int SiloCount = 10;

    // The auto-arrange default variants: top tiers so a fresh layout looks like an endgame farm.
    private const string DefaultLab = "ei_lab_6";
    private const string DefaultHoa = "ei_hoa_3";
    private const string DefaultHatchery = "ei_hatchery_universe";
    private const string DefaultMissionControl = "ei_mission_control_3";
    private const string DefaultFuel = "ei_fuel_tank_4";
    private const string DefaultDepot = "ei_depot_7";
    private const string DefaultTrophy = "ei_trophy_case2";
    // Sentinel: the API default when no ?hab= is chosen, so Standard() uses the mixed default hab row instead
    // of filling all 4 plots with one hab.
    public const string DefaultHabPlaceholder = "__default__";
    private static readonly string[] DefaultHabRow = ["hab_chicken_universe", "hab_chicken_universe", "hab_portal", "hab_monolith"];

    // The exact in-game silo position (FarmScene::updateSilo, disassembled): a 2-column row stepping back in X
    // every pair.
    public static float[] SiloPos(int i) =>
        [-6f * (i / 2) - 5f, 0f, (i % 2 == 0) ? 5.5f : -0.5f];

    // The standard farm. defaultHab = the hab stem for the 4-plot row. Self-placing pieces keep [0,0,0]; others
    // (trophy, mission control, artifact hall, fuel tank) are authored at mesh origin and need an explicit
    // world position.
    public static IReadOnlyList<Placed> Standard(string defaultHab = DefaultHabPlaceholder)
    {
        var p = new List<Placed>
        {
            // ei_farm_ground already bakes the paths, so ei_farm (Farm paths) is not auto-placed to avoid overlap.
            new("ei_farm_ground", [0, 0, 0], 0),
            new("ei_farm_hardscape", [0, 0, 0], 0),
            new("ei_farm_misc", [0, 0, 0], 0),
            new("ei_hyperloop_stop", [0, 0, 0], 0),
            new("ei_hyperloop_track", [0, 0, 0], 0),
            new("ei_farm_mailbox_full", [0, 0, 0], 0),
            new(DefaultTrophy, [-7, 0, 11], 0),
        };
        // Core buildings resolve from the fixed zone grid (ZoneLayout). Default tiers; StandardRecovered swaps
        // in the chosen tiers.
        p.AddRange(ZoneLayout.Resolve(DefaultLab, DefaultHoa, DefaultHatchery, DefaultMissionControl, DefaultFuel, DefaultDepot));

        // Hab row: 4 plots, evenly spaced. When the caller passes a specific hab it fills all 4; otherwise the
        // default mixed row.
        var habRow = defaultHab == DefaultHabPlaceholder ? DefaultHabRow : [defaultHab, defaultHab, defaultHab, defaultHab];
        for (var i = 0; i < 4; i++)
        {
            var x = (i - 1.5f) * HabSpacing;
            p.Add(new Placed(habRow[i], [x, 0, HabZ], 0));
        }

        for (var i = 0; i < SiloCount; i++)
            p.Add(new Placed("ei_silo_0_large", SiloPos(i), 0));

        return p;
    }

    // The singleton stems whose X position is the recovered farm-width-dependent formula.
    public sealed record SingletonPlacement(
        FarmPlacementRecovery.Vec3Model? MissionControl,
        FarmPlacementRecovery.Vec3Model? FuelTank,
        FarmPlacementRecovery.Vec3Model? Hoa);

    // The recovered singleton formula is not yet reconciled with the zone grid's local frame, so `rec` is
    // accepted for API compatibility but not applied; zone anchors drive placement unconditionally.
    public static IReadOnlyList<Placed> StandardRecovered(SingletonPlacement rec, float farmHalfWidth, string defaultHab = DefaultHabPlaceholder)
    {
        var list = Standard(defaultHab).ToList();
        list.RemoveAll(p => IsCoreZoneStem(p.Stem));
        list.AddRange(ZoneLayout.Resolve(DefaultLab, DefaultHoa, DefaultHatchery, DefaultMissionControl, DefaultFuel, DefaultDepot));
        return list;
    }

    private static bool IsCoreZoneStem(string s) =>
        s.StartsWith("ei_lab") || s.StartsWith("ei_afx_construction") || s.StartsWith("ei_hoa")
        || s.StartsWith("ei_hatchery") || s.StartsWith("ei_mission_control") || s.StartsWith("ei_fuel_tank")
        || s.StartsWith("ei_depot");
}
