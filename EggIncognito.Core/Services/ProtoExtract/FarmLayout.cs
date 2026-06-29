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
    // One placed element: the catalog stem + a world transform (position, Y rotation, scale).
    public sealed record Placed(string Stem, float[] Pos, float RotY, float Scale = 1f);

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
            // terrain (world origin)
            new("ei_farm_ground", [0, 0, 0], 0),
            new("ei_farm", [0, 0, 0], 0),
            new("ei_farm_hardscape", [0, 0, 0], 0),
            new("ei_farm_misc", [0, 0, 0], 0),
            // self-placing: mesh carries the offset.
            new("ei_depot_3", [0, 0, 0], 0), // right-near (z~7-12), in front of the road
            new("ei_hatchery_edible", [0, 0, 0], 0), // egg hatchery, between depot + lab (z~0.5-5.5)
            new("ei_lab_3", [0, 0, 0], 0), // research lab, BEHIND the depot (z~-6..0)
            new("ei_hyperloop_stop", [0, 0, 0], 0), // across the road (z~19-27)
            new("ei_hyperloop_track", [0, 0, 0], 0), // the hyperloop tube
            new("ei_farm_mailbox_full", [0, 0, 0], 0), // self-places near (-3, 11)
            // origin-authored: placed explicitly relative to the depot. FALLBACK positions; the recovered
            // overload (Standard with recovered placement) replaces the singleton X with the extracted formula.
            new("ei_mission_control_1", [16, 0, 9], 0), // RIGHT of the depot, near row
            new("ei_fuel_tank_2", [23, 0, 9], 0), // next to mission control
            new("ei_afx_construction_site", [16, 0, -3], 0), // artifact hall, BEHIND mission control
            new("ei_trophy_case", [-7, 0, 11], 0), // LEFT of the mailbox
        };

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

    // The singleton stems whose X position is the recovered farm-width-dependent formula.
    public sealed record SingletonPlacement(
        FarmPlacementRecovery.Vec3Model? MissionControl,
        FarmPlacementRecovery.Vec3Model? FuelTank,
        FarmPlacementRecovery.Vec3Model? Hoa);

    // The standard farm with the singleton X positions taken from the EXTRACTED placement formulas, evaluated at
    // the farm's actual half-width (the dynamic, adjacency-dependent offset the game computes from its farm-bound
    // state, approximated here from the placed buildings). Y/Z keep the authored fallback unless the recovered
    // axis is fully resolved (no residual struct field). When a model is absent/unrecovered the authored
    // fallback stands. defaultHab + the rest of the layout match Standard().
    public static IReadOnlyList<Placed> StandardRecovered(SingletonPlacement rec, float farmHalfWidth, string defaultHab = "hab_10k")
    {
        var list = Standard(defaultHab).ToList();
        var env = new Dictionary<string, double> { ["farmWidth"] = farmHalfWidth };

        float[] Apply(float[] authored, FarmPlacementRecovery.Vec3Model? m)
        {
            if (m is not { Ok: true } model || model.X is null) return authored;
            var x = ExprNode.IsFullyResolved(model.X) ? (float)ExprNode.Eval(model.X, env) : authored[0];
            var y = model.Y is not null && ExprNode.IsFullyResolved(model.Y) ? (float)ExprNode.Eval(model.Y, env) : authored[1];
            var z = model.Z is not null && ExprNode.IsFullyResolved(model.Z) ? (float)ExprNode.Eval(model.Z, env) : authored[2];
            return [x, y, z];
        }

        for (var i = 0; i < list.Count; i++)
        {
            var pl = list[i];
            float[]? np = pl.Stem switch
            {
                "ei_mission_control_1" => Apply(pl.Pos, rec.MissionControl),
                "ei_fuel_tank_2" => Apply(pl.Pos, rec.FuelTank),
                "ei_afx_construction_site" => Apply(pl.Pos, rec.Hoa),
                _ => null,
            };
            if (np is not null) list[i] = pl with { Pos = np };
        }
        return list;
    }
}
