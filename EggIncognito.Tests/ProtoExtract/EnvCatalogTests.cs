using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EnvCatalogTests
{
    [Fact]
    public void Presets_OnlyReferenceKnownPieces()
    {
        foreach (var preset in EnvCatalog.Presets)
            foreach (var stem in preset.Pieces)
                Assert.True(EnvCatalog.IsKnownPiece(stem), $"preset {preset.Id} references unknown piece {stem}");
    }

    [Fact]
    public void IsKnownPiece_RejectsTraversalAndUnknown()
    {
        Assert.True(EnvCatalog.IsKnownPiece("ei_farm_ground"));
        Assert.False(EnvCatalog.IsKnownPiece("../egginc"));
        Assert.False(EnvCatalog.IsKnownPiece("nope"));
    }

    [Fact]
    public void PresetById_FindsAndMisses()
    {
        Assert.NotNull(EnvCatalog.PresetById("farm_full"));
        Assert.Null(EnvCatalog.PresetById("missing"));
    }

    [Fact]
    public void Habs_AreAllKnownPieces()
    {
        Assert.NotEmpty(EnvCatalog.Habs);
        foreach (var h in EnvCatalog.Habs)
            Assert.True(EnvCatalog.IsKnownPiece(h.Stem), $"hab {h.Stem} not in Pieces allowlist");
    }
}
