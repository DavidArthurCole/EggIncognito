using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class ResearchCatalogExtractorTests {
    [Fact]
    public void Extract_SegmentsCommonAndEpic() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = ResearchCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(56, r.Entries.Count(e => !e.Epic));
        Assert.Equal(24, r.Entries.Count(e => e.Epic));
        Assert.Equal(r.Entries.Count, r.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Extract_DecodesKnownAnchors() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = ResearchCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        var comfy = r.Entries.Single(e => e.Id == "comfy_nests");
        Assert.False(comfy.Epic);
        Assert.Equal(50, comfy.MaxLevel);
        Assert.Equal("COMFORTABLE NESTS", comfy.Name);
        Assert.Equal("eggLayingRateMult", comfy.Dimension);
        Assert.Equal(ResearchCatalogExtractor.Combine.MulPlusOne, comfy.CombineMode);
        Assert.Equal(0.1, comfy.Magnitude!.Value, 1e-9);

        var nutritional = r.Entries.Single(e => e.Id == "nutritional_sup");
        Assert.Equal(40, nutritional.MaxLevel);
        Assert.Equal("eggValueMult", nutritional.Dimension);
        Assert.Equal(ResearchCatalogExtractor.Combine.MulPlusOne, nutritional.CombineMode);
        Assert.Equal(0.25, nutritional.Magnitude!.Value, 1e-9);

        var fleet = r.Entries.Single(e => e.Id == "vehicle_reliablity");
        Assert.Equal("maxFleetSize", fleet.Dimension);
        Assert.Equal(ResearchCatalogExtractor.Combine.Add, fleet.CombineMode);
        Assert.Equal(1.0, fleet.Magnitude);
        Assert.True(fleet.DimensionIsInt);

        var clucking = r.Entries.Single(e => e.Id == "coordinated_clucking");
        Assert.Equal("onscreenChickenMultMaxBase", clucking.Dimension);
        Assert.Equal(ResearchCatalogExtractor.Combine.Add, clucking.CombineMode);
        Assert.Equal(0.2, clucking.Magnitude!.Value, 1e-6);

        Assert.True(r.Entries.Single(e => e.Id == "hold_to_hatch").Epic);
    }

    [Fact]
    public void Extract_DecodesMostEffects() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = ResearchCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(r.Entries.Count(e => e.Magnitude is not null) >= 60);
    }
}
