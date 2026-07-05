using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Services.ProtoExtract;

// A game-like default farm layout: the standard set of farm elements at in-game plot positions, so the
// designer can one-click "Auto-arrange" a believable farm instead of freehand placing every piece.
//
// KEY FACT: most building meshes are authored at their real in-game plot position in their own vertex coords
// (depot z~7-12, hyperloop z~19-27 "across the road", lab x~4-10, mission control / hoa near origin), so they
// SELF-PLACE at world origin (0,0,0). Only the repeated rows need index math: the silo formula is exact from
// disassembly (FarmScene::updateSilo); the 4 habs have no code spacing constant (each slot transform is baked
// into its RPO), so the hab row uses a derived even spacing, pushed back so the ramps clear the path.
public static class FarmLayout
{
    // One placed element: the catalog stem + a world transform (position, Y rotation, scale). Recenter = the
    // renderer must recenter the mesh on its origin so Pos is the SOLE placement authority (the gravity-packed
    // core buildings, whose meshes carry a baked plot offset that would otherwise double-place them). Self-placing
    // pieces (terrain, hyperloop, mailbox, silos, habs) keep Recenter=false: their authored offset IS the layout.
    public sealed record Placed(string Stem, float[] Pos, float RotY, float Scale = 1f, bool Recenter = false);

    // Hab row layout EXTRACTED from GameController::getHabPosition(int) (1.35.6 symbolized, disasm). The game does
    // NOT use fixed hab X. Each hab i sits at Z = HabRowZ (a fixed back row), Y = HabRowY, and X = the running sum
    // of the earlier habs' widths plus HabGap between them, centered by width*0.5 (the `fmul ...,#0.5` + `#3.0`
    // constants in getHabPosition). Width is each hab's own mesh bbox (variable bounding box) so a wider hab pushes
    // the rest right, exactly as the user described. The renderer (playground.js) does the cumulative bbox-width
    // walk since it holds the meshes; C# supplies only the stems + these extracted constants.
    public const float HabRowZ = -10.5f; // getHabPosition ret[8]: float bits 0xC1280000 = -10.5 (row depth)
    public const float HabRowY = 0f; // habs sit on the ground plane
    public const float HabGap = 3f; // getHabPosition inter-hab gap constant (fadd d1, d1, #3.0)
    public const float HabHalfStep = 0.5f; // getHabPosition centering coefficient (fmul d0, d0, #0.5)
    private const float HabSpacing = 13f; // FALLBACK uniform X step when a hab's mesh bbox is unavailable (stopgap)
    private const float HabZ = HabRowZ; // alias for the placement Z used below
    private const int SiloCount = 10; // a full silo row

    // The auto-arrange default variants. These are the tiers the designer opens with (user-chosen top tiers so a
    // fresh layout looks like an endgame farm). Swap freely via the per-element variation dropdown.
    private const string DefaultLab = "ei_lab_6";
    private const string DefaultHoa = "ei_hoa_3";
    private const string DefaultHatchery = "ei_hatchery_universe";
    private const string DefaultMissionControl = "ei_mission_control_3";
    private const string DefaultFuel = "ei_fuel_tank_4";
    private const string DefaultDepot = "ei_depot_7";
    private const string DefaultTrophy = "ei_trophy_case2";
    // Sentinel: the API default when no ?hab= is chosen, so Standard() knows to use the mixed default hab row
    // instead of filling all 4 plots with one hab.
    public const string DefaultHabPlaceholder = "__default__";
    // The default 4-plot hab row (left -> right), the top hab tiers.
    private static readonly string[] DefaultHabRow = ["hab_chicken_universe", "hab_chicken_universe", "hab_portal", "hab_monolith"];

    // The exact in-game silo position (FarmScene::updateSilo, disassembled): a 2-column row stepping back in X
    // every pair. X = -6*floor(i/2) - 5; Y = 0; Z = (i even) ? 5.5 : -0.5.
    public static float[] SiloPos(int i) =>
        [-6f * (i / 2) - 5f, 0f, (i % 2 == 0) ? 5.5f : -0.5f];

    // The standard farm. defaultHab = the hab stem for the 4-plot row.
    //
    // Two placement classes:
    // - SELF-PLACING (pos [0,0,0]): the mesh vertices already sit at the real in-game plot (depot z~7-12,
    //   hyperloop z~19-27, lab z~-6..0, mailbox offset). Origin is correct.
    // - ORIGIN-AUTHORED (explicit pos): trophy, mission control, artifact hall, fuel tank are authored at the
    //   mesh origin, so they need an explicit world position or they overlap. Laid out relative to the
    //   self-placed depot (center ~x7,z9): near row z~9, back row z~-3.
    public static IReadOnlyList<Placed> Standard(string defaultHab = DefaultHabPlaceholder)
    {
        var p = new List<Placed>
        {
            // terrain (world origin). ei_farm_ground already bakes the paths, so ei_farm (Farm paths) is NOT
            // auto-placed: adding both overlaps the same path geometry. It stays in the palette for manual use.
            new("ei_farm_ground", [0, 0, 0], 0),
            new("ei_farm_hardscape", [0, 0, 0], 0),
            new("ei_farm_misc", [0, 0, 0], 0),
            // genuinely self-placing: the mesh carries the real plot offset (hyperloop spans the road, mailbox).
            new("ei_hyperloop_stop", [0, 0, 0], 0), // across the road (z~19-27)
            new("ei_hyperloop_track", [0, 0, 0], 0), // the hyperloop tube
            new("ei_farm_mailbox_full", [0, 0, 0], 0), // self-places near (-3, 11)
            new(DefaultTrophy, [-7, 0, 11], 0), // LEFT of the mailbox
        };
        // the core buildings (lab/hoa back, hatchery/mission-control/fuel mid, depot front) resolve from the
        // fixed zone grid (ZoneLayout), right of the silo field's path. They are placed ONLY here (not also
        // self-placed) so there is no duplicate. Default tiers; StandardRecovered swaps in the chosen tiers.
        p.AddRange(ZoneLayout.Resolve(DefaultLab, DefaultHoa, DefaultHatchery, DefaultMissionControl, DefaultFuel, DefaultDepot));

        // hab row: 4 plots, evenly spaced (no game spacing constant; positions are model-baked). When the caller
        // passes a specific hab (?hab=) it fills all 4; otherwise the default mixed row.
        var habRow = defaultHab == DefaultHabPlaceholder ? DefaultHabRow : [defaultHab, defaultHab, defaultHab, defaultHab];
        for (var i = 0; i < 4; i++)
        {
            var x = (i - 1.5f) * HabSpacing;
            p.Add(new Placed(habRow[i], [x, 0, HabZ], 0));
        }

        // silo row: the exact in-game 2-column formula, 10 silos.
        for (var i = 0; i < SiloCount; i++)
            p.Add(new Placed("ei_silo_0_large", SiloPos(i), 0));

        return p;
    }

    // The singleton stems whose X position is the recovered farm-width-dependent formula.
    public sealed record SingletonPlacement(
        FarmPlacementRecovery.Vec3Model? MissionControl,
        FarmPlacementRecovery.Vec3Model? FuelTank,
        FarmPlacementRecovery.Vec3Model? Hoa);

    // The standard farm's core buildings, via the fixed zone grid (ZoneLayout). The recovered per-singleton
    // formula (mission control / fuel / hoa world-frame X) is not yet reconciled with the zone grid's local
    // frame (tracked as a follow-up), so it is accepted for API compatibility but not applied; the zone anchors
    // drive placement unconditionally. Matches Standard() for everything else (terrain, habs, silos).
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
