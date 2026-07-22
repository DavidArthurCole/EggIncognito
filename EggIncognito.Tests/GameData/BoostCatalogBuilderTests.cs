using EggIncognito.Services;
using EggIncognito.Tests.ProtoExtract;

namespace EggIncognito.Tests.GameData;

public sealed class BoostCatalogBuilderTests {
    [Fact]
    public void Build_ProducesFullIdentityAndCostCatalog() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var configPath = FindConfig();
        if (configPath is null) return;

        var res = BoostCatalogBuilder.Build(bin, File.ReadAllText(configPath), "egginc-1.35.6");

        Assert.Equal(33, res.File.Boosts.Count);

        var printer = res.File.Boosts.Single(b => b.Id == "money_printer");
        Assert.Equal("MONEY PRINTER", printer.DisplayName);

        var beacon = res.File.Boosts.Single(b => b.Id == "boost_beacon_blue");
        Assert.NotNull(beacon.Description);
        Assert.StartsWith("Multiplies the effect of ALL other boosts", beacon.Description);
        Assert.Equal(1000, beacon.Price);
        Assert.Equal(1, beacon.TokenPrice);
        Assert.Equal(1000d, beacon.SeRequired);

        var prism = res.File.Boosts.Single(b => b.Id == "tachyon_prism_blue_v2");
        Assert.Equal("b_icon_tachyon_prism_blue", prism.IconAsset);
    }

    private static string? FindConfig() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null) {
            var candidate = Path.Combine(dir.FullName, "EggIncognito", "Endpoints", "default", "ei", "get_config.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
