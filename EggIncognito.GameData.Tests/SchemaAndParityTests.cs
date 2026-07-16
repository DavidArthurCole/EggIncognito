using EggIncognito.GameData;

namespace EggIncognito.GameData.Tests;

public sealed class SchemaAndParityTests
{
    private static readonly IGameDataProvider Provider = GameDataProvider.CreateDefault();

    private static readonly string[] HatcheryBoostIds =
    [
        "tachyon_prism_blue", "tachyon_prism_blue_v2", "tachyon_prism_blue_big",
        "tachyon_prism_purple", "tachyon_prism_purple_v2", "tachyon_prism_purple_big",
        "tachyon_prism_orange", "tachyon_prism_orange_big",
        "boost_beacon_blue", "boost_beacon_blue_big", "boost_beacon_purple", "boost_beacon_orange",
        "quantum_bulb", "dilithium_bulb"
    ];

    [Fact]
    public void Every_hatchery_boost_id_has_an_effect_row()
    {
        foreach (var id in HatcheryBoostIds)
        {
            Assert.NotNull(Provider.Resolve("boost", id));
        }
    }

    [Fact]
    public void Every_effect_carries_a_source_citation()
    {
        var all = Provider.Families.SelectMany(f => f.Effects);
        Assert.All(all, e => Assert.False(string.IsNullOrWhiteSpace(e.Source)));
    }

    [Fact]
    public void Every_effect_is_sourced_from_the_game_binary_or_wire()
    {
        var all = Provider.Families.SelectMany(f => f.Effects);
        Assert.All(all, e => Assert.True(
            e.Source.StartsWith("extracted@", StringComparison.Ordinal)
            || e.Source.StartsWith("binary@", StringComparison.Ordinal)
            || e.Source.StartsWith("captured@", StringComparison.Ordinal),
            $"{e.Family}:{e.Id} has non-device source '{e.Source}'"));
    }

    [Fact]
    public void Every_row_meta_validates_against_its_family_schema()
    {
        foreach (var family in Provider.Families)
        {
            if (family.MetaSchema is null) continue;
            foreach (var effect in family.Effects)
            {
                var ex = Record.Exception(() => new EffectRow(family.MetaSchema, effect.Id, effect.Meta));
                Assert.Null(ex);
            }
        }
    }

    [Fact]
    public void Unknown_meta_field_is_rejected()
    {
        var schema = new EffectSchema([new EffectField("a", EffectFieldType.Int)]);
        Assert.Throws<GameDataSchemaException>(() =>
            new EffectRow(schema, "x", new Dictionary<string, object> { ["b"] = 1 }));
    }

    [Fact]
    public void Missing_required_meta_field_is_rejected()
    {
        var schema = new EffectSchema([new EffectField("a", EffectFieldType.Int)]);
        Assert.Throws<GameDataSchemaException>(() =>
            new EffectRow(schema, "x", new Dictionary<string, object>()));
    }

    [Fact]
    public void Wrong_meta_type_is_rejected()
    {
        var schema = new EffectSchema([new EffectField("a", EffectFieldType.Int)]);
        Assert.Throws<GameDataSchemaException>(() =>
            new EffectRow(schema, "x", new Dictionary<string, object> { ["a"] = "notint" }));
    }

    [Fact]
    public void Research_max_levels_match_extracted_binary()
    {
        Assert.Equal(250, Provider.Resolve("research", "internal_hatchery5")!.MaxLevel);
        Assert.Equal(20, Provider.Resolve("research", "epic_internal_incubators")!.MaxLevel);
        Assert.Equal(20, Provider.Resolve("research", "int_hatch_calm")!.MaxLevel);
    }
}
