using EggIncognito.GameData;
using EggIncognito.Services;
using EggIncognito.Tests.ProtoExtract;
using Xunit;

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

    [Fact]
    public void Committed_catalog_matches_builder_output() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        string? configJson = null;
        foreach (var rel in new[]
        {
            "../../../../EggIncognito/Endpoints/default/ei/get_config.json",
            "../../../../../EggIncognito/Endpoints/default/ei/get_config.json",
            "../../../../Endpoints/default/ei/get_config.json"
        }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) { configJson = File.ReadAllText(full); break; }
        }
        if (configJson is null) return;

        var built = BoostCatalogBuilder.Build(bin, configJson, "egginc-1.35.6").File;
        var committed = BoostCatalog.Load();

        Assert.Equal(built.Boosts.Count, committed.Boosts.Count);
        foreach (var b in built.Boosts) {
            var c = committed.Find(b.Id);
            Assert.NotNull(c);
            Assert.Equal(b.DisplayName, c!.DisplayName);
            Assert.Equal(b.Description, c.Description);
            Assert.Equal(b.Price, c.Price);
            Assert.Equal(b.TokenPrice, c.TokenPrice);
            Assert.Equal(b.SeRequired, c.SeRequired);
            Assert.Equal(b.IconAsset, c.IconAsset);
        }
    }
}
