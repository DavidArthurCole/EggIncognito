using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Services.ProtoExtract;


public static class FarmLayout {



    public sealed record Placed(string Stem, float[] Pos, float RotY, float Scale = 1f, bool Recenter = false);





    public const float HabRowZ = -10.5f;
    public const float HabRowY = 0f;
    public const float HabGap = 3f;
    public const float HabHalfStep = 0.5f;
    private const float HabSpacing = 13f;
    private const float HabZ = HabRowZ;
    private const int SiloCount = 10;


    private const string DefaultLab = "ei_lab_6";
    private const string DefaultHoa = "ei_hoa_3";
    private const string DefaultHatchery = "ei_hatchery_universe";
    private const string DefaultMissionControl = "ei_mission_control_3";
    private const string DefaultFuel = "ei_fuel_tank_4";
    private const string DefaultDepot = "ei_depot_7";
    private const string DefaultTrophy = "ei_trophy_case2";


    public const string DefaultHabPlaceholder = "__default__";
    private static readonly string[] DefaultHabRow = ["hab_chicken_universe", "hab_chicken_universe", "hab_portal", "hab_monolith"];



    public static float[] SiloPos(int i) =>
        [-6f * (i / 2) - 5f, 0f, (i % 2 == 0) ? 5.5f : -0.5f];




    public static IReadOnlyList<Placed> Standard(string defaultHab = DefaultHabPlaceholder) {
        var p = new List<Placed>
        {

            new("ei_farm_ground", [0, 0, 0], 0),
            new("ei_farm_hardscape", [0, 0, 0], 0),
            new("ei_farm_misc", [0, 0, 0], 0),
            new("ei_hyperloop_stop", [0, 0, 0], 0),
            new("ei_hyperloop_track", [0, 0, 0], 0),
            new("ei_farm_mailbox_full", [0, 0, 0], 0),
            new(DefaultTrophy, [-7, 0, 11], 0),
        };


        p.AddRange(ZoneLayout.Resolve(DefaultLab, DefaultHoa, DefaultHatchery, DefaultMissionControl, DefaultFuel, DefaultDepot));



        var habRow = defaultHab == DefaultHabPlaceholder ? DefaultHabRow : [defaultHab, defaultHab, defaultHab, defaultHab];
        for (var i = 0; i < 4; i++) {
            var x = (i - 1.5f) * HabSpacing;
            p.Add(new Placed(habRow[i], [x, 0, HabZ], 0));
        }

        for (var i = 0; i < SiloCount; i++)
            p.Add(new Placed("ei_silo_0_large", SiloPos(i), 0));

        return p;
    }


    public sealed record SingletonPlacement(
        FarmPlacementRecovery.Vec3Model? MissionControl,
        FarmPlacementRecovery.Vec3Model? FuelTank,
        FarmPlacementRecovery.Vec3Model? Hoa);



    public static IReadOnlyList<Placed> StandardRecovered(SingletonPlacement rec, float farmHalfWidth, string defaultHab = DefaultHabPlaceholder) {
        var list = Standard(defaultHab).ToList();
        list.RemoveAll(p => IsCoreZoneStem(p.Stem));
        list.AddRange(ZoneLayout.Resolve(DefaultLab, DefaultHoa, DefaultHatchery, DefaultMissionControl, DefaultFuel, DefaultDepot));
        return list;
    }

    private static bool IsCoreZoneStem(string s) =>
        s.StartsWith("ei_lab", StringComparison.Ordinal) || s.StartsWith("ei_afx_construction", StringComparison.Ordinal) || s.StartsWith("ei_hoa", StringComparison.Ordinal)
        || s.StartsWith("ei_hatchery", StringComparison.Ordinal) || s.StartsWith("ei_mission_control", StringComparison.Ordinal) || s.StartsWith("ei_fuel_tank", StringComparison.Ordinal)
        || s.StartsWith("ei_depot", StringComparison.Ordinal);
}
