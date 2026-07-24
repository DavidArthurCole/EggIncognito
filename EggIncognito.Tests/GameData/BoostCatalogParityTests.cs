using EggIncognito.GameData;

namespace EggIncognito.Tests.GameData;

public class BoostCatalogParityTests {
    [Fact]
    public void Committed_catalog_loads_with_expected_shape() {
        var cat = BoostCatalog.Load();

        Assert.Equal(33, cat.Boosts.Count);
        Assert.Equal("MONEY PRINTER", cat.Find("money_printer")!.DisplayName);
        Assert.StartsWith("Multiplies the effect of ALL other boosts", cat.Find("boost_beacon_blue")!.Description);
        Assert.Equal("b_icon_tachyon_prism_blue", cat.Find("tachyon_prism_blue_v2")!.IconAsset);
        Assert.Equal(1000, cat.Find("boost_beacon_blue")!.Price);
        Assert.All(cat.Boosts, b => Assert.False(string.IsNullOrEmpty(b.IconAsset)));
        Assert.Equal("binary", cat.Provenance["identity"].Origin);
        Assert.Equal("ei/get_config", cat.Provenance["cost"].Locator);
        Assert.Equal("derived", cat.Provenance["iconAsset"].Origin);
    }

}
