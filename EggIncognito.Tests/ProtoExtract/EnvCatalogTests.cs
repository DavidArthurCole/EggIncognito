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
    public void Habs_AreAllKnownPieces()
    {
        Assert.NotEmpty(EnvCatalog.Habs);
        foreach (var h in EnvCatalog.Habs)
            Assert.True(EnvCatalog.IsKnownPiece(h.Stem), $"hab {h.Stem} not in Pieces allowlist");
    }
}
