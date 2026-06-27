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

    private const float HabSpacing = 13f;   // X gap between the 4 hab plots (hab ~12 wide)
    private const float HabZ = -16f;        // pushed back so hab ramps (z to ~0) clear the path
    private const int SiloCount = 10;       // a full silo row

    // The exact in-game silo position (FarmScene::updateSilo, disassembled): a 2-column row stepping back in X
    // every pair. X = -6*floor(i/2) - 5; Y = 0; Z = (i even) ? 5.5 : -0.5.
    public static float[] SiloPos(int i) =>
        [-6f * (i / 2) - 5f, 0f, (i % 2 == 0) ? 5.5f : -0.5f];

    // The standard farm. defaultHab = the hab stem for the 4-plot row. The self-placing buildings sit at
    // origin (their mesh carries the offset); habs + silos are positioned here.
    public static IReadOnlyList<Placed> Standard(string defaultHab = "hab_10k")
    {
        var p = new List<Placed>
        {
            // terrain (world origin)
            new("ei_farm_ground", [0, 0, 0], 0),
            new("ei_farm", [0, 0, 0], 0),
            new("ei_farm_hardscape", [0, 0, 0], 0),
            new("ei_farm_misc", [0, 0, 0], 0),
            // self-placing buildings: the mesh vertices already sit at the in-game plot, so origin is correct.
            new("ei_depot_3", [0, 0, 0], 0),            // near side (z~7-12), in front of the road
            new("ei_hyperloop_stop", [0, 0, 0], 0),     // across the road (z~19-27)
            new("ei_lab_3", [0, 0, 0], 0),              // research lab
            new("ei_mission_control_1", [0, 0, 0], 0),
            new("ei_hoa_1", [0, 0, 0], 0),
            new("ei_trophy_case", [0, 0, 0], 0),
            new("ei_farm_mailbox_full", [0, 0, 0], 0),
        };

        // hab row: 4 plots, evenly spaced (no game spacing constant; positions are model-baked), pushed back.
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
}
