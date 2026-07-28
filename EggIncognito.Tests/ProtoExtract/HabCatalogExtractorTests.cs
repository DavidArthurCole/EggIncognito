using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class HabCatalogExtractorTests {
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
    public void Extract_CapacitiesMatchHabCapacityExtractor() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var catalog = HabCatalogExtractor.Extract(bin);
        var caps = HabCapacityExtractor.Extract(bin);
        Assert.True(catalog.Ok, catalog.Diagnostics);
        Assert.True(caps.Ok, caps.Diagnostics);
        Assert.Equal(caps.Capacities, catalog.Entries.Select(e => e.Capacity));
    }
}
