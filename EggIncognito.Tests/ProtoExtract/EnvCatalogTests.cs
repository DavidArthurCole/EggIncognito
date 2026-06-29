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
    public void StandardRecovered_AppliesRecoveredX_AtFarmWidth()
    {
        // a recovered missionControl model: X = (2.8 + farmWidth) + 1.5, fully resolved (no residual field).
        var x = new EggIncognito.Services.ProtoExtract.Decomp.Binary(
            EggIncognito.Services.ProtoExtract.Decomp.BinOp.Add,
            new EggIncognito.Services.ProtoExtract.Decomp.Binary(
                EggIncognito.Services.ProtoExtract.Decomp.BinOp.Add,
                new EggIncognito.Services.ProtoExtract.Decomp.Const(2.8),
                new EggIncognito.Services.ProtoExtract.Decomp.Input("farmWidth")),
            new EggIncognito.Services.ProtoExtract.Decomp.Const(1.5));
        var mc = new EggIncognito.Services.ProtoExtract.Decomp.FarmPlacementRecovery.Vec3Model(
            true, "missionControlPos", x, null, null, 0, "ok");
        var rec = new FarmLayout.SingletonPlacement(mc, null, null);

        var placed = FarmLayout.StandardRecovered(rec, farmHalfWidth: 13.5f);
        var mcPlaced = placed.First(p => p.Stem == "ei_mission_control_1");
        Assert.Equal(2.8f + 13.5f + 1.5f, mcPlaced.Pos[0], 2); // recovered X
        // Y/Z stay the authored fallback (the model's Y/Z are null/unresolved).
        Assert.Equal(9f, mcPlaced.Pos[2], 2);
    }

    [Fact]
    public void StandardRecovered_NoModel_UsesFallback()
    {
        var rec = new FarmLayout.SingletonPlacement(null, null, null);
        var placed = FarmLayout.StandardRecovered(rec, 13.5f);
        var mc = placed.First(p => p.Stem == "ei_mission_control_1");
        Assert.Equal(16f, mc.Pos[0], 2); // the authored fallback X
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
