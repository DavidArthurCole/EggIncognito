using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class EggCatalogExtractorTests {
    private static readonly string[] BaseEggNames = [
        "EDIBLE", "SUPERFOOD", "MEDICAL", "ROCKET FUEL", "SUPER MATERIAL", "FUSION", "QUANTUM", "CRISPR",
        "TACHYON", "GRAVITON", "DILITHIUM", "PRODIGY", "TERRAFORM", "ANTIMATTER", "DARK MATTER", "A.I.",
        "NEBULA", "UNIVERSE", "ENLIGHTENMENT"
    ];

    private static readonly double[] BaseEggValues = [
        0.25, 1.25, 6.25, 30, 150, 700, 3000, 12500, 50000, 175000, 525000, 1500000, 1e7, 1e9, 1e11, 1e12,
        1.5e13, 1e14, 1e-7
    ];

    private static readonly double[] BaseEggHatcheryExtents = [
        12, 12, 13, 12.5, 13.2, 15.1, 17.6, 14.1, 20.3, 12.9, 15.5, 17.3, 15.8, 24, 18.5, 19.8, 18.5, 19.4, 13.5
    ];

    [Fact]
    public void Read_EnumeratesEveryBaseEggInRecordOrder() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = EggCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(BaseEggNames.Length, r.Entries.Count);
        for (int i = 0; i < BaseEggNames.Length; i++) {
            Assert.Equal(i, r.Entries[i].Index);
            Assert.Equal(BaseEggNames[i], r.Entries[i].Name);
        }
    }

    [Fact]
    public void Read_DecodesTheFullBaseValueSequence() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = EggCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(BaseEggValues.Length, r.Entries.Count);
        for (int i = 0; i < BaseEggValues.Length; i++)
            Assert.Equal(BaseEggValues[i], r.Entries[i].BaseValue, 9);
    }

    [Fact]
    public void Read_DecodesTheFullHatcheryExtentSequence() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = EggCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(BaseEggHatcheryExtents.Length, r.Entries.Count);
        for (int i = 0; i < BaseEggHatcheryExtents.Length; i++) {
            Assert.Equal(BaseEggHatcheryExtents[i], r.Entries[i].HatcheryExtent, 3);
        }
    }

    [Fact]
    public void Read_EveryEggCarriesAFiniteHatcheryExtent() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = EggCatalogExtractor.Read(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.NotEmpty(r.Entries);
        foreach (var e in r.Entries) {
            Assert.True(double.IsFinite(e.HatcheryExtent), $"egg {e.Index} ({e.Name}) extent is not finite");
            Assert.True(e.HatcheryExtent > 0, $"egg {e.Index} ({e.Name}) extent is not positive");
        }
    }
}
