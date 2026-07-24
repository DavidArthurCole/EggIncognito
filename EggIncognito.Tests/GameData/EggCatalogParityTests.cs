using EggIncognito.GameData;

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

}
