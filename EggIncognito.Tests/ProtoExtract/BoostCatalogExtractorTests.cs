using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class BoostCatalogExtractorTests {
    [Fact]
    public void Extracts_boost_catalog_with_display_names_and_descriptions() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = BoostCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(33, r.Entries.Count);

        Assert.Equal("MONEY PRINTER", Name(r, "money_printer"));
        Assert.Equal("TACHYON PRISM", Name(r, "tachyon_prism_blue_v2"));
        Assert.Equal("LEGENDARY SOUL BEACON", Name(r, "soul_beacon_orange"));

        var beacon = r.Entries.Single(e => e.Id == "boost_beacon_blue");
        Assert.NotNull(beacon.Description);
        Assert.StartsWith("Multiplies the effect of ALL other boosts", beacon.Description);
    }

    private static string? Name(BoostCatalogExtractor.Result r, string id)
        => r.Entries.Single(e => e.Id == id).DisplayName;
}
