namespace EggIncognito.GameData.Tests;

public sealed class GameDataParsingTests {
    private const string BoostsJson = """
        {
          "binaryVersion": "test-1.0",
          "rows": [
            { "id": "boost_a", "target": "IHR", "combineMode": "Mul", "magnitude": 10, "meta": { "kind": "InternalHatcheryMult", "durationSeconds": 600 } },
            { "id": "beacon_a", "target": "BeaconMult", "combineMode": "Add", "magnitude": 1, "meta": { "kind": "BeaconMult", "durationSeconds": 600 } }
          ]
        }
        """;

    private const string ResearchJson = """
        {
          "binaryVersion": "test-1.0",
          "rows": [
            { "id": "res_a", "target": "IHR", "combineMode": "MulPlusOne", "magnitude": 0.05, "maxLevel": 10, "meta": { "name": "Test Research", "epic": false } }
          ]
        }
        """;

    private const string HabsJson = """
        {
          "binaryVersion": "test-1.0",
          "rows": [
            { "id": "hab_a", "target": "HabCapacity", "combineMode": "Add", "magnitude": 100, "meta": { "habId": 1 } }
          ]
        }
        """;

    private const string ArtifactsJson = """
        {
          "binaryVersion": "test-1.0",
          "rows": [
            { "id": "art_a", "target": "IHR", "combineMode": "MulPlusOne", "magnitude": 0.1, "meta": { "boost": "boost_a" } }
          ]
        }
        """;

    private const string BoostCatalogJson = """
        {
          "binaryVersion": "test-1.0",
          "boosts": [
            { "id": "boost_a", "displayName": "Boost A", "price": 200, "tokenPrice": 1, "iconAsset": "b_icon_a" }
          ]
        }
        """;

    private const string ColleggtiblesJson = """
        {
          "gameVersion": "test-1.0",
          "eggs": [
            { "identifier": "carbon-fiber", "dimension": "SHIPPING_CAPACITY", "tierValues": [1.01, 1.02, 1.03, 1.05] }
          ],
          "contractEggMap": { "contract-a": "carbon-fiber" }
        }
        """;

    private const string EggsJson = """
        {
          "binaryVersion": "test-1.0",
          "eggs": [
            { "index": 1, "name": "Edible", "baseValue": 0.1 }
          ]
        }
        """;

    private const string DimensionsJson = """
        {
          "binaryVersion": "test-1.0",
          "dimensions": [ "EARNINGS", "SHIPPING_CAPACITY" ]
        }
        """;

    private const string MissionsJson = """
        {
          "binaryVersion": "test-1.0",
          "missions": [
            { "id": "mission_a", "displayName": "Mission A", "goal": "collect" }
          ]
        }
        """;

    private const string VehiclesJson = """
        {
          "binaryVersion": "test-1.0",
          "vehicles": [
            { "index": 0, "name": "Trike", "capacity": 5000 }
          ]
        }
        """;

    private static Dictionary<string, string> AllDocs() => new(StringComparer.Ordinal) {
        ["boosts"] = BoostsJson,
        ["research"] = ResearchJson,
        ["habs"] = HabsJson,
        ["artifacts"] = ArtifactsJson,
        ["boost-catalog"] = BoostCatalogJson,
        ["colleggtibles"] = ColleggtiblesJson,
        ["eggs"] = EggsJson,
        ["dimensions"] = DimensionsJson,
        ["missions"] = MissionsJson,
        ["vehicles"] = VehiclesJson
    };

    [Fact]
    public void FromDocuments_builds_provider_from_all_ten_documents() {
        var provider = GameDataProvider.FromDocuments(AllDocs());
        Assert.Equal(4, provider.Families.Count);
        Assert.NotNull(provider.Resolve(Families.Boost, "boost_a"));
        Assert.NotNull(provider.BoostCatalog.Find("boost_a"));
        Assert.NotNull(provider.Colleggtibles.Find("carbon-fiber"));
        Assert.NotNull(provider.EggCatalog.Find(1));
        Assert.True(provider.Dimensions.Contains("EARNINGS"));
        Assert.NotNull(provider.Missions.Find("mission_a"));
        Assert.NotNull(provider.Vehicles.Find(0));
    }

    [Fact]
    public void FromDocuments_missing_document_throws_naming_the_id() {
        var docs = AllDocs();
        docs.Remove("missions");
        var ex = Assert.Throws<GameDataSchemaException>(() => GameDataProvider.FromDocuments(docs));
        Assert.Contains("missions", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentIds_lists_all_ten_ids() {
        Assert.Equal(10, GameDataProvider.DocumentIds.Length);
        var docs = AllDocs();
        foreach (string id in GameDataProvider.DocumentIds) Assert.True(docs.ContainsKey(id), id);
    }

    [Fact]
    public void Validate_accepts_every_synthetic_document() {
        foreach ((string id, string json) in AllDocs()) GameDataProvider.Validate(id, json);
    }

    [Fact]
    public void Validate_rejects_invalid_json() =>
        Assert.Throws<GameDataSchemaException>(() => GameDataProvider.Validate("eggs", "not json"));

    [Fact]
    public void Validate_rejects_unknown_id() {
        var ex = Assert.Throws<GameDataSchemaException>(() => GameDataProvider.Validate("nope", "{}"));
        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Family_parses_rows_and_exposes_meta() {
        var family = new BoostFamily(EffectDataLoader.Parse(BoostsJson));
        Assert.Equal(2, family.Effects.Count);
        Assert.Equal("test-1.0", family.BinaryVersion);
        var effect = family.Find("boost_a");
        Assert.NotNull(effect);
        Assert.Equal(600, effect.MetaInt("durationSeconds"));
        Assert.Equal("InternalHatcheryMult", effect.MetaString("kind"));
    }

    [Fact]
    public void Family_row_missing_required_meta_field_throws() {
        const string json = """
            {
              "binaryVersion": "test-1.0",
              "rows": [
                { "id": "boost_bad", "target": "IHR", "combineMode": "Mul", "magnitude": 10, "meta": { "kind": "InternalHatcheryMult" } }
              ]
            }
            """;
        var ex = Assert.Throws<GameDataSchemaException>(() => new BoostFamily(EffectDataLoader.Parse(json)));
        Assert.Contains("durationSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Effective_folds_synthetic_magnitudes_across_families() {
        var provider = GameDataProvider.FromDocuments(AllDocs());
        double value = provider.Effective(EffectTarget.IHR, 1, new Dictionary<string, int> {
            ["boost_a"] = 1,
            ["res_a"] = 2
        });
        Assert.Equal(10 * (1 + 0.1), value, 10);
    }

    [Fact]
    public void ColleggtibleCatalog_requires_exactly_four_tier_values() {
        const string json = """
            {
              "gameVersion": "test-1.0",
              "eggs": [
                { "identifier": "short", "dimension": "EARNINGS", "tierValues": [1.01, 1.02, 1.03] }
              ]
            }
            """;
        var ex = Assert.Throws<GameDataSchemaException>(() => ColleggtibleCatalog.Parse(json));
        Assert.Contains("4 tierValues", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DimensionCodes_map_is_intact() {
        Assert.Equal(10, ColleggtibleCatalog.DimensionCodes.Count);
        Assert.Equal(0, ColleggtibleCatalog.DimensionCodes["INVALID"]);
        Assert.Equal(1, ColleggtibleCatalog.DimensionCodes["EARNINGS"]);
        Assert.Equal(2, ColleggtibleCatalog.DimensionCodes["AWAY_EARNINGS"]);
        Assert.Equal(3, ColleggtibleCatalog.DimensionCodes["INTERNAL_HATCHERY_RATE"]);
        Assert.Equal(4, ColleggtibleCatalog.DimensionCodes["EGG_LAYING_RATE"]);
        Assert.Equal(5, ColleggtibleCatalog.DimensionCodes["SHIPPING_CAPACITY"]);
        Assert.Equal(6, ColleggtibleCatalog.DimensionCodes["HAB_CAPACITY"]);
        Assert.Equal(7, ColleggtibleCatalog.DimensionCodes["VEHICLE_COST"]);
        Assert.Equal(8, ColleggtibleCatalog.DimensionCodes["HAB_COST"]);
        Assert.Equal(9, ColleggtibleCatalog.DimensionCodes["RESEARCH_COST"]);
    }
}
