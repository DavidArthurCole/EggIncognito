using EggIncognito.GameData;

namespace EggIncognito.GameData.Tests;

public sealed class SchemaAndParityTests {
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
    public void Every_hatchery_boost_id_has_an_effect_row() {
        foreach (var id in HatcheryBoostIds) {
            Assert.NotNull(Provider.Resolve("boost", id));
        }
    }

    [Fact]
    public void Every_hatchery_boost_carries_a_cost_from_get_config() {
        foreach (var id in HatcheryBoostIds) {
            var e = Provider.Resolve("boost", id)!;
            Assert.True(e.TryMeta("price", out _), $"{id} missing price");
            Assert.True(e.TryMeta("tokenPrice", out _), $"{id} missing tokenPrice");
            Assert.True(e.MetaInt("price") >= 0);
            Assert.True(e.MetaInt("tokenPrice") >= 0);
        }
    }

    [Fact]
    public void Known_boost_costs_match_captured_get_config() {
        var e = Provider.Resolve("boost", "tachyon_prism_orange")!;
        Assert.Equal(12000, e.MetaInt("price"));
        Assert.Equal(4, e.MetaInt("tokenPrice"));

        var beacon = Provider.Resolve("boost", "boost_beacon_orange")!;
        Assert.Equal(50000, beacon.MetaInt("price"));
        Assert.Equal(8, beacon.MetaInt("tokenPrice"));
    }

    private static readonly string[] AllowedOrigins = ["binary", "config", "fixture", "derived"];

    private static IEnumerable<(string Dataset, string Aspect, ProvenanceSource Source)> AllProvenance() {
        foreach (var f in Provider.Families) {
            foreach (var (aspect, src) in f.Provenance)
                yield return (f.Key, aspect, src);
        }

        foreach (var (aspect, src) in Provider.Colleggtibles.Provenance)
            yield return ("colleggtibles", aspect, src);
        foreach (var (aspect, src) in Provider.BoostCatalog.Provenance)
            yield return ("boost-catalog", aspect, src);
        foreach (var (aspect, src) in Provider.EggCatalog.Provenance)
            yield return ("egg-catalog", aspect, src);
        foreach (var (aspect, src) in Provider.Dimensions.Provenance)
            yield return ("dimension", aspect, src);
        foreach (var (aspect, src) in Provider.Missions.Provenance)
            yield return ("mission", aspect, src);
        foreach (var (aspect, src) in Provider.Vehicles.Provenance)
            yield return ("vehicle", aspect, src);
    }

    [Fact]
    public void Every_dataset_carries_file_level_provenance() {
        Assert.All(Provider.Families, f => Assert.NotEmpty(f.Provenance));
        Assert.NotEmpty(Provider.Colleggtibles.Provenance);
        Assert.All(AllProvenance(), p =>
            Assert.False(string.IsNullOrWhiteSpace(p.Source.Origin), $"{p.Dataset}:{p.Aspect} has empty origin"));
    }

    [Fact]
    public void Every_provenance_origin_is_from_the_game_binary_or_wire() => Assert.All(AllProvenance(), p => Assert.Contains(p.Source.Origin, AllowedOrigins));

    [Fact]
    public void No_provenance_is_sourced_from_egg9000() {
        Assert.All(AllProvenance(), p => {
            Assert.DoesNotContain("egg9000", p.Source.Origin, StringComparison.OrdinalIgnoreCase);
            if (p.Source.Locator is { } loc)
                Assert.DoesNotContain("egg9000", loc, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Every_row_meta_validates_against_its_family_schema() {
        foreach (var family in Provider.Families) {
            if (family.MetaSchema is null) continue;
            foreach (var effect in family.Effects) {
                var ex = Record.Exception(() => new EffectRow(family.MetaSchema, effect.Id, effect.Meta));
                Assert.Null(ex);
            }
        }
    }

    [Fact]
    public void Unknown_meta_field_is_rejected() {
        var schema = new EffectSchema([new EffectField("a", EffectFieldType.Int)]);
        Assert.Throws<GameDataSchemaException>(() =>
            new EffectRow(schema, "x", new Dictionary<string, object> { ["b"] = 1 }));
    }

    [Fact]
    public void Missing_required_meta_field_is_rejected() {
        var schema = new EffectSchema([new EffectField("a", EffectFieldType.Int)]);
        Assert.Throws<GameDataSchemaException>(() =>
            new EffectRow(schema, "x", new Dictionary<string, object>()));
    }

    [Fact]
    public void Wrong_meta_type_is_rejected() {
        var schema = new EffectSchema([new EffectField("a", EffectFieldType.Int)]);
        Assert.Throws<GameDataSchemaException>(() =>
            new EffectRow(schema, "x", new Dictionary<string, object> { ["a"] = "notint" }));
    }

    [Fact]
    public void Research_max_levels_match_extracted_binary() {
        Assert.Equal(250, Provider.Resolve("research", "internal_hatchery5")!.MaxLevel);
        Assert.Equal(20, Provider.Resolve("research", "epic_internal_incubators")!.MaxLevel);
        Assert.Equal(20, Provider.Resolve("research", "int_hatch_calm")!.MaxLevel);
    }
}
