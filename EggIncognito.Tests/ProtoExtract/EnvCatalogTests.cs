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
    public void Standard_PlacesCoreZonesAtRowAnchors()
    {
        // FarmLayout.Standard gives each core building its ROW's initial anchor (real left-to-right spacing
        // is applied afterward by playground.js's repackZoneRow, using real mesh width the C# side cannot see).
        var placed = FarmLayout.Standard("hab_10k");
        var lab = placed.First(p => p.Stem == "ei_lab_6");
        var hoa = placed.First(p => p.Stem == "ei_hoa_3");
        var hatchery = placed.First(p => p.Stem == "ei_hatchery_universe");
        var mc = placed.First(p => p.Stem == "ei_mission_control_3");
        var fuel = placed.First(p => p.Stem == "ei_fuel_tank_4");
        var depot = placed.First(p => p.Stem == "ei_depot_7");

        Assert.Equal(ZoneLayout.BackRowZ, lab.Pos[2], 2);
        Assert.Equal(ZoneLayout.BackRowZ, hoa.Pos[2], 2);
        Assert.Equal(ZoneLayout.MidRowZ, hatchery.Pos[2], 2);
        Assert.Equal(ZoneLayout.MidRowZ, mc.Pos[2], 2);
        Assert.Equal(ZoneLayout.MidRowZ, fuel.Pos[2], 2);
        Assert.Equal(ZoneLayout.FrontRowZ, depot.Pos[2], 2);
    }

    [Fact]
    public void StandardRecovered_FallsBackToZoneLayout_WhenNoFormula()
    {
        var placed = FarmLayout.StandardRecovered(new FarmLayout.SingletonPlacement(null, null, null), farmHalfWidth: 20f);
        Assert.Contains(placed, p => p.Stem == "ei_hatchery_universe");
        Assert.Contains(placed, p => p.Stem == "ei_depot_7");
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
