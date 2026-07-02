using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EnvCatalogTests
{
    [Fact]
    public void IsKnownPiece_RejectsTraversalAndUnknown()
    {
        Assert.True(EnvCatalog.IsKnownPiece("ei_farm_ground"));
        Assert.False(EnvCatalog.IsKnownPiece("../egginc"));
        Assert.False(EnvCatalog.IsKnownPiece("nope"));
    }

    [Fact]
    public void Pieces_IncludeBuildingsAndHabs()
    {
        Assert.Contains(EnvCatalog.Pieces, p => p.Stem == "ei_silo_0_large");
        Assert.Contains(EnvCatalog.Pieces, p => p.Stem == "hab_10k");
        Assert.True(EnvCatalog.Pieces.Count >= 12);
    }

    [Fact]
    public void TerrainPieces_AreSingleton_AndGrouped()
    {
        var ground = EnvCatalog.Pieces.First(p => p.Stem == "ei_farm_ground");
        Assert.True(ground.Singleton);
        Assert.Equal("Terrain", ground.Group);
        // habs + storage are repeatable.
        Assert.False(EnvCatalog.Pieces.First(p => p.Stem == "hab_10k").Singleton);
        Assert.Equal("Habs", EnvCatalog.Pieces.First(p => p.Stem == "hab_10k").Group);
        Assert.Equal("Storage", EnvCatalog.Pieces.First(p => p.Stem == "ei_silo_0_large").Group);
    }

    [Fact]
    public void FarmLayout_Standard_HasFourHabs_TenSilos_NoCoop_KnownStems()
    {
        var placed = FarmLayout.Standard("hab_10k");
        Assert.All(placed, p => Assert.True(EnvCatalog.IsKnownPiece(p.Stem), $"unknown stem {p.Stem}"));
        Assert.Contains(placed, p => p.Stem == "ei_farm_ground");
        var habs = placed.Where(p => p.Stem == "hab_10k").ToList();
        Assert.Equal(4, habs.Count);
        Assert.Equal(4, habs.Select(h => h.Pos[0]).Distinct().Count());
        Assert.Equal(10, placed.Count(p => p.Stem == "ei_silo_0_large"));
        Assert.DoesNotContain(placed, p => p.Stem == "coop");
        // the hyperloop station is part of the standard farm (across the road).
        Assert.Contains(placed, p => p.Stem == "ei_hyperloop_stop");
    }

    [Fact]
    public void Family_ReturnsHabTiers()
    {
        var fam = EnvCatalog.Family("hab_10k");
        Assert.True(fam.Count >= 5);
        Assert.All(fam, p => Assert.Equal("hab", p.Family));
        // a non-family piece has no siblings.
        Assert.Empty(EnvCatalog.Family("ei_farm_ground"));
    }

    [Fact]
    public void SiloPos_MatchesGameFormula()
    {
        // FarmScene::updateSilo: X = -6*floor(i/2) - 5; Z alternates 5.5 / -0.5; Y = 0.
        Assert.Equal(new[] { -5f, 0f, 5.5f }, FarmLayout.SiloPos(0));
        Assert.Equal(new[] { -5f, 0f, -0.5f }, FarmLayout.SiloPos(1));
        Assert.Equal(new[] { -11f, 0f, 5.5f }, FarmLayout.SiloPos(2));
        Assert.Equal(new[] { -11f, 0f, -0.5f }, FarmLayout.SiloPos(3));
        Assert.Equal(new[] { -17f, 0f, 5.5f }, FarmLayout.SiloPos(4));
    }

    [Fact]
    public void CoreRows_PacksThreeRows_LeftToRight_GravityPushesRight()
    {
        var core = FarmLayout.CoreRows("ei_lab_3", "ei_afx_construction_site", "ei_hatchery_edible",
            "ei_mission_control_1", "ei_fuel_tank_2", "ei_depot_3");

        // rows at the three Z bands.
        Assert.Equal(FarmLayout.RowBackZ, core.First(p => p.Stem == "ei_lab_3").Pos[2], 2);
        Assert.Equal(FarmLayout.RowMidZ, core.First(p => p.Stem == "ei_hatchery_edible").Pos[2], 2);
        Assert.Equal(FarmLayout.RowFrontZ, core.First(p => p.Stem == "ei_depot_3").Pos[2], 2);

        // gravity: in the mid row, mission control sits RIGHT of the hatchery, fuel tank RIGHT of mission control.
        var hatch = core.First(p => p.Stem == "ei_hatchery_edible").Pos[0];
        var mc = core.First(p => p.Stem == "ei_mission_control_1").Pos[0];
        var fuel = core.First(p => p.Stem == "ei_fuel_tank_2").Pos[0];
        Assert.True(mc > hatch, "mission control is right of hatchery");
        Assert.True(fuel > mc, "fuel tank is right of mission control");
    }

    [Fact]
    public void CoreRows_WiderLeftBuilding_PushesRightFurther()
    {
        // a wider mid-row first building shifts the ones to its right. The depot (half 5.0) vs fuel (half 2.5):
        // packing a wide-then-narrow row spaces them more than narrow-then-narrow.
        var wide = FarmLayout.CoreRows("ei_lab_3", "ei_afx_construction_site", "ei_depot_3", "ei_fuel_tank_2", "ei_fuel_tank_2", "ei_depot_3");
        var narrow = FarmLayout.CoreRows("ei_lab_3", "ei_afx_construction_site", "ei_fuel_tank_2", "ei_fuel_tank_2", "ei_fuel_tank_2", "ei_depot_3");
        var wideSecond = wide.Where(p => p.Pos[2] == FarmLayout.RowMidZ).ElementAt(1).Pos[0];
        var narrowSecond = narrow.Where(p => p.Pos[2] == FarmLayout.RowMidZ).ElementAt(1).Pos[0];
        Assert.True(wideSecond > narrowSecond, "a wider left building pushes the next one further right");
    }

    [Fact]
    public void StandardRecovered_RecoveredFormula_ShiftsCoreLeftStart()
    {
        var x = new EggIncognito.Services.ProtoExtract.Decomp.Binary(
            EggIncognito.Services.ProtoExtract.Decomp.BinOp.Add,
            new EggIncognito.Services.ProtoExtract.Decomp.Binary(
                EggIncognito.Services.ProtoExtract.Decomp.BinOp.Add,
                new EggIncognito.Services.ProtoExtract.Decomp.Const(2.8),
                new EggIncognito.Services.ProtoExtract.Decomp.Input("farmWidth")),
            new EggIncognito.Services.ProtoExtract.Decomp.Const(1.5));
        var mc = new EggIncognito.Services.ProtoExtract.Decomp.FarmPlacementRecovery.Vec3Model(
            true, "missionControlPos", x, null, null, 0, "ok");

        var wide = FarmLayout.StandardRecovered(new FarmLayout.SingletonPlacement(mc, null, null), farmHalfWidth: 20f);
        var narrow = FarmLayout.StandardRecovered(new FarmLayout.SingletonPlacement(mc, null, null), farmHalfWidth: 5f);
        // the recovered layout repacks with the default core variants; the hatchery is the default (Universe).
        var wideHatch = wide.First(p => p.Stem == "ei_hatchery_universe").Pos[0];
        var narrowHatch = narrow.First(p => p.Stem == "ei_hatchery_universe").Pos[0];
        // a bigger farm width pushes the whole packed core further right (the recovered formula drives the start).
        Assert.True(wideHatch > narrowHatch, "wider farm shifts the core right");
    }

    [Fact]
    public void Family_FuelTank_HasFourVariants()
    {
        var fam = EnvCatalog.Family("ei_fuel_tank_2");
        Assert.Equal(4, fam.Count);
        Assert.All(fam, p => Assert.Equal("fuel", p.Family));
        Assert.Contains(fam, p => p.Stem == "ei_fuel_tank_1");
        Assert.Contains(fam, p => p.Stem == "ei_fuel_tank_4");
        Assert.All(fam, p => Assert.True(EnvCatalog.IsKnownPiece(p.Stem)));
    }

    [Fact]
    public void Family_Silo_SwapsBaseAndAlt()
    {
        var fam = EnvCatalog.Family("ei_silo_0_large");
        Assert.Equal(2, fam.Count);
        Assert.All(fam, p => Assert.Equal("silo", p.Family));
        Assert.Contains(fam, p => p.Stem == "ei_silo");
        Assert.Contains(fam, p => p.Stem == "ei_silo_0_large");
    }

    [Fact]
    public void Standard_DefaultHabRow_IsMixedTopTiers_And_DefaultVariants()
    {
        var placed = FarmLayout.Standard(); // no ?hab= -> the mixed default row
        var habs = placed.Where(p => p.Stem.StartsWith("hab_")).OrderBy(p => p.Pos[0]).ToList();
        Assert.Equal(4, habs.Count);
        // left -> right: Chicken Universe x2, Planet Portal, Monolith
        Assert.Equal(new[] { "hab_chicken_universe", "hab_chicken_universe", "hab_portal", "hab_monolith" },
            habs.Select(h => h.Stem).ToArray());
        // all habs share the extracted fixed row Z.
        Assert.All(habs, h => Assert.Equal(FarmLayout.HabRowZ, h.Pos[2], 2));

        // default core variants.
        Assert.Contains(placed, p => p.Stem == "ei_lab_6");
        Assert.Contains(placed, p => p.Stem == "ei_hoa_3");
        Assert.Contains(placed, p => p.Stem == "ei_hatchery_universe");
        Assert.Contains(placed, p => p.Stem == "ei_mission_control_3");
        Assert.Contains(placed, p => p.Stem == "ei_fuel_tank_4");
        Assert.Contains(placed, p => p.Stem == "ei_depot_7");
        Assert.Contains(placed, p => p.Stem == "ei_trophy_case2");
    }

    [Fact]
    public void Standard_ExplicitHab_FillsAllFourPlots()
    {
        var placed = FarmLayout.Standard("hab_10k");
        Assert.Equal(4, placed.Count(p => p.Stem == "hab_10k"));
    }

    [Fact]
    public void AssetTypeOf_MapsRepresentativeStems()
    {
        Assert.Equal("Depot3", EnvCatalog.AssetTypeOf("ei_depot_3"));
        Assert.Equal("Lab1", EnvCatalog.AssetTypeOf("ei_lab_1"));
        Assert.Equal("Hab1K", EnvCatalog.AssetTypeOf("hab_1k"));
        Assert.Equal("Hab10K", EnvCatalog.AssetTypeOf("hab_10k"));
        Assert.Equal("FuelTank3", EnvCatalog.AssetTypeOf("ei_fuel_tank_3"));
        Assert.Equal("MissionControl2", EnvCatalog.AssetTypeOf("ei_mission_control_2"));
        Assert.Equal("HatcheryEdible", EnvCatalog.AssetTypeOf("ei_hatchery_edible"));
        Assert.Null(EnvCatalog.AssetTypeOf("ei_farm_ground"));
        Assert.Null(EnvCatalog.AssetTypeOf("nope"));
    }

    [Fact]
    public void Vehicles_AreKnown_AndGrouped()
    {
        Assert.True(EnvCatalog.IsKnownPiece("ei_vehicle_semi"));
        Assert.True(EnvCatalog.IsKnownPiece("ei_vehicle_pickup"));
        Assert.True(EnvCatalog.IsKnownPiece("ei_vehicle_mega_semi"));
        var vehicles = EnvCatalog.Pieces.Where(p => p.Group == "Vehicles").ToList();
        Assert.True(vehicles.Count >= 10, $"expected 10+ vehicles, got {vehicles.Count}");
        Assert.All(vehicles, v => Assert.False(v.Singleton));
        Assert.Null(EnvCatalog.AssetTypeOf("ei_vehicle_semi"));
    }

    [Fact]
    public void Ships_AreKnown_AndGrouped()
    {
        Assert.True(EnvCatalog.IsKnownPiece("ei_ship_egg_shuttle"));
        var ships = EnvCatalog.Pieces.Where(p => p.Group == "Ships").ToList();
        Assert.True(ships.Count >= 5, $"expected 5+ ships, got {ships.Count}");
        Assert.Null(EnvCatalog.AssetTypeOf("ei_ship_egg_shuttle"));
    }

    [Fact]
    public void Habs_AreAllKnownPieces()
    {
        Assert.NotEmpty(EnvCatalog.Habs);
        foreach (var h in EnvCatalog.Habs)
            Assert.True(EnvCatalog.IsKnownPiece(h.Stem), $"hab {h.Stem} not in Pieces allowlist");
    }
}
