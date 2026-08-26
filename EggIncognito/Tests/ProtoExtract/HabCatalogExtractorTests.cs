using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class HabCatalogExtractorTests {
    private static readonly double[] ExpectedWidths = [
        3, 4, 4.5, 4.5, 4.5, 5, 5.5, 12.2, 12.5, 7.5, 15.5, 9.5, 16.5, 8.2, 12, 17, 14, 11, 9.5
    ];

    private static readonly double[] ExpectedExtents = [
        5, 6, 9, 10, 15, 25, 25, 25, 20, 20, 25, 15, 25, 15, 18, 25, 25, 25, 20
    ];

    [Fact]
    public void Extract_DecodesAllNineteenHabs() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = HabCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(19, r.Entries.Count);
        Assert.Equal(19, r.Entries.Count(e => e.Name is not null));
        Assert.Equal(Enumerable.Range(0, 19), r.Entries.Select(e => e.Index));
    }

    [Fact]
    public void Extract_DecodesKnownAnchors() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = HabCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);

        Assert.Equal("COOP", r.Entries[0].Name);
        Assert.Equal(250, r.Entries[0].Capacity);
        Assert.Equal("SHACK", r.Entries[1].Name);
        Assert.Equal(500, r.Entries[1].Capacity);
        Assert.Equal("SUPER SHACK", r.Entries[2].Name);
        Assert.Equal(1000, r.Entries[2].Capacity);
        Assert.Equal(600_000_000, r.Entries[18].Capacity);
        Assert.Equal("CHICKEN UNIVERSE", r.Entries[18].Name);
    }

    [Fact]
    public void Extract_DecodesWidthTable() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = HabCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(ExpectedWidths.Length, r.Entries.Count);
        for (int i = 0; i < ExpectedWidths.Length; i++) Assert.Equal(ExpectedWidths[i], r.Entries[i].Width, 6);
    }

    [Fact]
    public void Extract_DecodesExtentTable() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = HabCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(ExpectedExtents.Length, r.Entries.Count);
        for (int i = 0; i < ExpectedExtents.Length; i++) Assert.Equal(ExpectedExtents[i], r.Entries[i].Extent, 6);
    }

    [Fact]
    public void Extract_DepthIsUniformExceptChickenUniverse() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = HabCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        for (int i = 0; i < 18; i++) Assert.Equal(2.2, r.Entries[i].Depth, 5);
        Assert.Equal(4.0, r.Entries[18].Depth, 5);
    }
}
