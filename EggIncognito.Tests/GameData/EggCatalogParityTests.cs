using EggIncognito.GameData;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tests.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.GameData;

public class EggCatalogParityTests {
    [Fact]
    public void Committed_catalog_loads_with_expected_shape() {
        var cat = EggCatalog.Load();

        Assert.Equal(18, cat.Eggs.Count);
        Assert.Null(cat.Find(0)!.Name);
        Assert.Equal(0.25, cat.Find(0)!.BaseValue);
        Assert.Equal("MEDICAL", cat.Find(1)!.Name);
        Assert.Equal("CRISPR", cat.Find(6)!.Name);
        Assert.Equal("UNIVERSE", cat.Find(16)!.Name);
        Assert.Equal(1e14, cat.Find(16)!.BaseValue);
        Assert.Equal("ENLIGHTENMENT", cat.Find(17)!.Name);
        Assert.Equal(17, cat.Eggs.Count(e => e.Name is not null));
        Assert.Equal("binary", cat.Provenance["identity"].Origin);
        Assert.Equal("eggdata", cat.Provenance["baseValue"].Locator);
        Assert.Equal("decoded", cat.Provenance["baseValue"].Method);
    }

    [Fact]
    public void Committed_catalog_matches_extractor_output() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var extracted = EggCatalogExtractor.Read(bin);
        Assert.True(extracted.Ok, extracted.Diagnostics);

        var committed = EggCatalog.Load();
        Assert.Equal(extracted.Entries.Count, committed.Eggs.Count);
        foreach (var e in extracted.Entries) {
            var c = committed.Find(e.Index);
            Assert.NotNull(c);
            Assert.Equal(e.Name, c!.Name);
            Assert.Equal(e.BaseValue, c.BaseValue);
        }
    }
}
