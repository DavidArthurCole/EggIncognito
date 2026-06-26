using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EnvCatalogTests
{
    [Fact]
    public void Presets_OnlyReferenceKnownPieces()
    {
        foreach (var preset in EnvCatalog.Presets)
            foreach (var pp in preset.Pieces)
                Assert.True(EnvCatalog.IsKnownPiece(pp.Stem), $"preset {preset.Id} references unknown piece {pp.Stem}");
    }

    [Fact]
    public void HabRowPreset_PlacesFourHabsAtDistinctX()
    {
        var p = EnvCatalog.PresetById("farm_habs")!;
        var habs = p.Pieces.Where(pp => pp.Stem.StartsWith("hab_")).ToList();
        Assert.Equal(4, habs.Count);
        Assert.Equal(4, habs.Select(h => h.Offset[0]).Distinct().Count());
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
