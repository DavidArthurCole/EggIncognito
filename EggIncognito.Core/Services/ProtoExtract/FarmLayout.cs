namespace EggIncognito.Services.ProtoExtract;

// A game-like default farm layout: the standard set of farm elements at in-game-style plot positions, so the
// designer can one-click "Auto-arrange" a believable farm instead of freehand placing every piece.
//
// Egg Inc bakes plot positions into compiled C++ (FarmScene::updateHab(i), updateSilo(i), fuelTankPos(), ...),
// not a data file. Disassembly of the 1.35.7 arm64 binary (FarmScene::updateSilo @0x10008e080) gave the EXACT
// silo formula, used verbatim below. The 4 habs have NO spacing constant in code (each hab's slot transform
// is baked into its RPO model), so the hab row uses a derived even spacing. The fixed singletons (fuel tank,
// HOA, mission control) are runtime-computed from the live farm width (GameController fields), unavailable
// offline, so they keep hand-tuned positions. Approximations stay in the farm-ground scale (ground ~130x110).
public static class FarmLayout
{
    // One placed element in the layout: the catalog stem + a world transform (position, Y rotation, scale).
    public sealed record Placed(string Stem, float[] Pos, float RotY, float Scale = 1f);

    private const float HabSpacing = 14f;   // X gap between the 4 hab plots (no game constant; hab ~12 wide + margin)
    private const float HabZ = -8f;         // habs sit toward the back of the farm
    private const int SiloCount = 6;

    // The exact in-game silo position (FarmScene::updateSilo, disassembled): a 2-column row stepping back in X
    // every pair. X = -6*floor(i/2) - 5; Y = 0; Z = (i even) ? 5.5 : -0.5.
    public static float[] SiloPos(int i) =>
        [-6f * (i / 2) - 5f, 0f, (i % 2 == 0) ? 5.5f : -0.5f];

    // The standard farm: terrain underlay + a hab row + the game's silo row + storage + structures. defaultHab
    // is the hab stem used for the 4-plot row (e.g. the player's current hab).
    public static IReadOnlyList<Placed> Standard(string defaultHab = "hab_10k")
    {
        var p = new List<Placed>
        {
            // terrain (singletons, world origin)
            new("ei_farm_ground", [0, 0, 0], 0),
            new("ei_farm", [0, 0, 0], 0),
            new("ei_farm_hardscape", [0, 0, 0], 0),
            new("ei_farm_misc", [0, 0, 0], 0),
            // structures: hand-tuned (in-game pos is runtime-computed from farm width, unavailable offline)
            new("coop", [0, 0, 2], 0),
            new("ei_farm_mailbox_full", [-3, 0, 1], 0),
            new("ei_fuel_tank_2", [-12, 0, 4f], 0),  // game Z ~4.2; X needs runtime farm width, tuned here
        };

        // hab row: 4 plots, evenly spaced (no game spacing constant exists; positions are model-baked).
        for (var i = 0; i < 4; i++)
        {
            var x = (i - 1.5f) * HabSpacing;
            p.Add(new Placed(defaultHab, [x, 0, HabZ], 0));
        }

        // silo row: the exact in-game 2-column formula.
        for (var i = 0; i < SiloCount; i++)
            p.Add(new Placed("ei_silo_0_large", SiloPos(i), 0));

        // a depot, near its own authored mesh offset.
        p.Add(new Placed("ei_depot_3", [6, 0, 12], 0));

        return p;
    }
}
