using EggIncognito.GameData;

namespace EggIncognito.GameData.Tests;

public sealed class ProviderTests {
    private static readonly IGameDataProvider Provider = GameDataProvider.CreateDefault();

    [Fact]
    public void Loads_all_four_families() {
        Assert.Equal(4, Provider.Families.Count);
        Assert.All(new[] { "boost", "research", "hab", "artifact" },
            k => Assert.NotNull(Provider.Family(k)));
    }

    [Theory]
    [InlineData("boost", "tachyon_prism_orange")]
    [InlineData("research", "internal_hatchery5")]
    [InlineData("hab", "CHICKEN_UNIVERSE")]
    [InlineData("artifact", "ORNATE_GUSSET:4:3")]
    public void Resolves_each_family_by_id_without_game_client(string family, string id) => Assert.NotNull(Provider.Resolve(family, id));

    [Fact]
    public void Hab_base_capacities_match_extracted_binary() {
        Assert.Equal(250, Provider.Resolve("hab", "COOP")!.Magnitude);
        Assert.Equal(10_000_000, Provider.Resolve("hab", "HAB_10K")!.Magnitude);
        Assert.Equal(600_000_000, Provider.Resolve("hab", "CHICKEN_UNIVERSE")!.Magnitude);
    }

    [Fact]
    public void Ihr_common_research_is_additive() {
        var effective = Provider.Effective(EffectTarget.IHR, 0, new Dictionary<string, int> {
            ["internal_hatchery1"] = 10,
            ["internal_hatchery2"] = 10
        });
        Assert.Equal(10 * 2 + 10 * 5, effective);
    }

    [Fact]
    public void Ihr_epic_incubators_multiply_after_common_add() {
        var effective = Provider.Effective(EffectTarget.IHR, 0, new Dictionary<string, int> {
            ["internal_hatchery1"] = 10,
            ["epic_internal_incubators"] = 20
        });
        Assert.Equal((10 * 2) * (1 + 20 * 0.05), effective, 6);
    }

    [Fact]
    public void Hab_capacity_research_is_multiplicative_on_base() {
        var effective = Provider.Effective(EffectTarget.HabCapacity, 100, new Dictionary<string, int> {
            ["hab_capacity1"] = 8
        });
        Assert.Equal(100 * (1 + 8 * 0.05), effective, 6);
    }

    [Fact]
    public void Hen_house_ac_is_egg_laying_not_hab_capacity() => Assert.Equal(EffectTarget.EggLayingRate, Provider.Resolve("research", "hen_house_ac")!.Target);

    [Fact]
    public void Tachyon_deflector_is_coop_egg_laying_not_hatch() => Assert.Equal(EffectTarget.CoopEggLaying, Provider.Resolve("artifact", "TACHYON_DEFLECTOR:4:3")!.Target);
}
