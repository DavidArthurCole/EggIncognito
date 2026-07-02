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

    private const float HabSpacing = 13f; // X gap between the 4 hab plots (hab ~12 wide)
    private const float HabZ = -10f; // ramp front (hab z max ~0 local) meets the path edge
    private const int SiloCount = 10; // a full silo row

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
    public static IReadOnlyList<Placed> Standard(string defaultHab = "hab_10k")
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
            new("ei_trophy_case", [-7, 0, 11], 0), // LEFT of the mailbox
        };
        // the core buildings (lab/hoa back, hatchery/mission-control/fuel mid, depot front) are the three gravity-
        // packed rows, right of the silo field's path. They are placed ONLY here (not also self-placed) so there is
        // no duplicate. Default tiers; the recovered overload swaps in the chosen tiers + keeps the packing.
        p.AddRange(CoreRows("ei_lab_3", "ei_afx_construction_site", "ei_hatchery_edible", "ei_mission_control_1", "ei_fuel_tank_2", "ei_depot_3"));

        // hab row: 4 plots, evenly spaced (no game spacing constant; positions are model-baked).
        for (var i = 0; i < 4; i++)
        {
            var x = (i - 1.5f) * HabSpacing;
            p.Add(new Placed(defaultHab, [x, 0, HabZ], 0));
        }

        // silo row: the exact in-game 2-column formula, 10 silos.
        for (var i = 0; i < SiloCount; i++)
            p.Add(new Placed("ei_silo_0_large", SiloPos(i), 0));

        return p;
    }

    // The farm "core" buildings laid out as the game does: three rows, each a left-to-right sequence, gravity-
    // packed so a building further left pushes the ones to its right when it is wider (the variable-size buildings
    // grow with tier). Row Z values place back/mid/front relative to the road. Footprint widths are per-stem (the
    // STOPGAP approximation until extracted from the mesh bounds / FarmScene spacing; marked below).
    //
    // Row 1 (back):  Research Lab, Hall of Artifacts
    // Row 2 (mid):   Hatchery, Mission Control, Fuel Tank
    // Row 3 (front): Depot
    public const float RowBackZ = -6f;   // research lab / hoa row (furthest from road, nearest the habs)
    public const float RowMidZ = 4f;     // hatchery / mission control / fuel row (bodies are ~5 deep: ±2.5)
    public const float RowFrontZ = 13f;  // depot row: clears the mid row's deep bodies (z up to ~6.5) + the depot's own depth so it never lands inside the hatchery
    private const float RowGap = 2.5f;   // even gap between adjacent buildings in a row (in-game look)
    // The rows pack rightward from the right edge of the silo field's connecting path ("path 2"). The silo field
    // is all negative-X (rightmost column at X=-5, half ~2.5, so its right edge ~-2.5); path 2 + a gap puts the
    // rows' left bound just past world origin. STOPGAP value; the real path X is in the FarmScene terrain layout.
    private const float CoreLeftX = 2f;

    // Approximate footprint half-widths per building (X half-extent). STOPGAP: hand-measured from the meshes; the
    // real per-tier footprint is in the mesh bounds / FarmScene element-width table @0x10226a3c8 = [3.2,4.75,7.2,
    // 1.1,2.2,1.0]. Replace with extracted widths.
    private static float HalfWidth(string stem) => stem switch
    {
        var s when s.StartsWith("ei_lab") => 4.5f,
        var s when s.StartsWith("ei_hoa") || s.StartsWith("ei_afx_construction") => 4.0f,
        var s when s.StartsWith("ei_hatchery") => 4.0f,
        var s when s.StartsWith("ei_mission_control") => 4.5f,
        var s when s.StartsWith("ei_fuel_tank") => 2.5f,
        var s when s.StartsWith("ei_depot") => 5.0f,
        var s when s.StartsWith("ei_trophy") => 1.5f,
        _ => 3.0f,
    };

    // Pack one row left-to-right from startX: each building's center = previous center + prevHalf + gap + thisHalf.
    // Returns the placed positions (X only; Z + the stems come from the caller). The hatchery is placed by its
    // LEFT EDGE, not its center: it renders pin-left (recenterX=min) so a tier swap grows it to the right, so its
    // layout Pos.X must be the left edge to stay aligned with how it renders.
    private static List<Placed> PackRow(float startX, float z, params string[] stems)
    {
        var outp = new List<Placed>();
        float cursor = startX;
        float prevHalf = 0;
        bool first = true;
        foreach (var stem in stems)
        {
            var half = HalfWidth(stem);
            cursor += first ? half : prevHalf + RowGap + half;
            // Recenter=true: a packed core building is positioned by THIS X/Z, so its mesh centers on its origin
            // (else its baked plot offset double-places it, e.g. the depot across the road). The hatchery is the
            // exception: it pins its LEFT edge, so its Pos.X = the left edge (cursor - half).
            float posX = IsLeftPinned(stem) ? cursor - half : cursor;
            outp.Add(new Placed(stem, [posX, 0f, z], 0, Recenter: true));
            prevHalf = half;
            first = false;
        }
        return outp;
    }

    // The hatchery + depot render pinned to their left edge (recenterX=min), so the layout places them by left
    // edge too. Both share a fixed left/dock edge near the road and grow rightward with tier/size.
    private static bool IsLeftPinned(string stem) =>
        stem.StartsWith("ei_hatchery", StringComparison.Ordinal) || stem.StartsWith("ei_depot", StringComparison.Ordinal);

    // The farm core as the three gravity-packed rows. Variable building stems (tier-dependent) are passed in;
    // widening one shifts everything to its right. Used by the recovered layout; the terrain/habs/silos come from
    // Standard(). The Z rows + the per-stem widths are the stopgap approximation noted above.
    public static IReadOnlyList<Placed> CoreRows(string lab, string hoa, string hatchery, string missionControl, string fuel, string depot)
    {
        var core = new List<Placed>();
        core.AddRange(PackRow(CoreLeftX, RowBackZ, lab, hoa));
        core.AddRange(PackRow(CoreLeftX, RowMidZ, hatchery, missionControl, fuel));
        core.AddRange(PackRow(CoreLeftX, RowFrontZ, depot));
        return core;
    }

    // The singleton stems whose X position is the recovered farm-width-dependent formula.
    public sealed record SingletonPlacement(
        FarmPlacementRecovery.Vec3Model? MissionControl,
        FarmPlacementRecovery.Vec3Model? FuelTank,
        FarmPlacementRecovery.Vec3Model? Hoa);

    // The standard farm using the recovered mission-control X formula to set the core rows' left start, so the
    // gravity-packed core shifts with the farm width exactly as the game does (the extracted formula is
    // X = perConst + farmWidth + offset). The rest (terrain, habs, silos) matches Standard(). When the formula is
    // unavailable the fixed CoreLeftX stands. The chosen tiers swap into the rows + keep the packing.
    public static IReadOnlyList<Placed> StandardRecovered(SingletonPlacement rec, float farmHalfWidth, string defaultHab = "hab_10k")
    {
        // The recovered mission-control formula (X = perConst + farmWidth + offset) is in the GAME's coordinate
        // frame, not this stopgap silo-anchored frame, so using it directly as the row left-start scatters the
        // core. For now the rows pack from the fixed silo-edge start (CoreLeftX); the farm-width term only widens
        // the start a touch so a bigger farm reads slightly more spread. Replace wholesale once the rows are laid
        // out in the game's real frame (then the recovered formula drives them exactly).
        var env = new Dictionary<string, double> { ["farmWidth"] = farmHalfWidth };
        float leftStart = CoreLeftX;
        if (rec.MissionControl is { Ok: true, X: { } mx } && ExprNode.IsFullyResolved(mx))
        {
            // nudge: scale the start gently with farm width, clamped, instead of taking the raw game-frame X.
            var w = (float)ExprNode.Eval(mx, env);
            leftStart = CoreLeftX + Math.Clamp((w - 16f) * 0.1f, -2f, 4f);
        }

        var list = Standard(defaultHab).ToList();
        // re-pack the core rows from the derived left start (replace the default-start core entries).
        list.RemoveAll(p => IsCoreStem(p.Stem));
        list.AddRange(RepackCore(leftStart));
        return list;
    }

    private static bool IsCoreStem(string s) =>
        s.StartsWith("ei_lab") || s.StartsWith("ei_afx_construction") || s.StartsWith("ei_hoa")
        || s.StartsWith("ei_hatchery") || s.StartsWith("ei_mission_control") || s.StartsWith("ei_fuel_tank")
        || s.StartsWith("ei_depot");

    private static IReadOnlyList<Placed> RepackCore(float leftStart)
    {
        var core = new List<Placed>();
        core.AddRange(PackRow(leftStart, RowBackZ, "ei_lab_3", "ei_afx_construction_site"));
        core.AddRange(PackRow(leftStart, RowMidZ, "ei_hatchery_edible", "ei_mission_control_1", "ei_fuel_tank_2"));
        core.AddRange(PackRow(leftStart, RowFrontZ, "ei_depot_3"));
        return core;
    }
}
