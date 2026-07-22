using EggIncognito.GameData;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tests.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.GameData;

public class DimensionCatalogParityTests {
    [Fact]
    public void Committed_catalog_loads_with_expected_shape() {
        var cat = DimensionCatalog.Load();

        Assert.Equal(9, cat.Dimensions.Count);
        Assert.Equal("bd-earnings", cat.Dimensions[0]);
        Assert.Equal("bd-coop-egglayingrate", cat.Dimensions[8]);
        Assert.True(cat.Contains("bd-soul-power"));
        Assert.False(cat.Contains("bd-unknown"));
        Assert.Equal("binary", cat.Provenance["identity"].Origin);
        Assert.Equal("boostmanager", cat.Provenance["identity"].Locator);
    }

    [Fact]
    public void Committed_catalog_matches_extractor_output() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var extracted = DimensionCatalogExtractor.Extract(bin);
        Assert.True(extracted.Ok, extracted.Diagnostics);

        var committed = DimensionCatalog.Load();
        Assert.Equal(extracted.Ids, committed.Dimensions);
    }
}
