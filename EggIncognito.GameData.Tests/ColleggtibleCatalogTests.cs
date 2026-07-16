using EggIncognito.GameData;

namespace EggIncognito.GameData.Tests;

public sealed class ColleggtibleCatalogTests
{
    private static readonly IColleggtibleCatalog Catalog = GameDataProvider.CreateDefault().Colleggtibles;

    [Fact]
    public void Loads_all_twelve_eggs()
    {
        Assert.Equal(12, Catalog.Eggs.Count);
    }

    [Fact]
    public void Every_egg_has_four_tiers_and_a_valid_dimension()
    {
        var validCodes = ColleggtibleCatalog.DimensionCodes.Values.ToHashSet();
        foreach (var egg in Catalog.Eggs)
        {
            Assert.Equal(4, egg.TierValues.Count);
            Assert.Contains(egg.Dimension, validCodes);
            Assert.NotEqual(0, egg.Dimension);
        }
    }

    [Theory]
    [InlineData("easter", 3, 1.05)]
    [InlineData("pegg", 6, 1.05)]
    [InlineData("silicon", 4, 1.05)]
    public void Known_stat_eggs_map_to_expected_dimension_and_top_tier(string id, int dimension, double topTier)
    {
        var egg = Catalog.Find(id);
        Assert.NotNull(egg);
        Assert.Equal(dimension, egg!.Dimension);
        Assert.Equal(topTier, egg.TierValues[^1]);
    }

    [Fact]
    public void Easter_tier_values_match_capture()
    {
        var egg = Catalog.Find("easter");
        Assert.NotNull(egg);
        Assert.Equal(new[] { 1.01, 1.02, 1.03, 1.05 }, egg!.TierValues);
    }

    [Fact]
    public void Contract_map_carries_known_links()
    {
        Assert.Equal("pegg", Catalog.ContractEggMap["model-kits-2026"]);
        Assert.Equal("wood", Catalog.ContractEggMap["more-housing-2026"]);
        Assert.Equal("ice", Catalog.ContractEggMap["thermal-runaway-2026"]);
    }

    [Fact]
    public void Unknown_egg_resolves_null()
    {
        Assert.Null(Catalog.Find("no-such-egg"));
    }
}
