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
    public void FarmLayout_Standard_PlacesKnownStemsAndFourHabs()
    {
        var placed = FarmLayout.Standard("hab_10k");
        Assert.All(placed, p => Assert.True(EnvCatalog.IsKnownPiece(p.Stem), $"unknown stem {p.Stem}"));
        // ground + the 4-plot hab row present.
        Assert.Contains(placed, p => p.Stem == "ei_farm_ground");
        var habs = placed.Where(p => p.Stem == "hab_10k").ToList();
        Assert.Equal(4, habs.Count);
        Assert.Equal(4, habs.Select(h => h.Pos[0]).Distinct().Count());
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
    public void Habs_AreAllKnownPieces()
    {
        Assert.NotEmpty(EnvCatalog.Habs);
        foreach (var h in EnvCatalog.Habs)
            Assert.True(EnvCatalog.IsKnownPiece(h.Stem), $"hab {h.Stem} not in Pieces allowlist");
    }
}
