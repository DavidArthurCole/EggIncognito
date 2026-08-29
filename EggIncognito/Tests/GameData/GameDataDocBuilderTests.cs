using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.GameData;

namespace EggIncognito.Tests.GameData;

public class GameDataDocBuilderTests {
    [Fact]
    public void BuildMissions_DropsGoallessRows_AndValidates() {
        var doc = GameDataDocBuilders.BuildMissions([
            new MissionCatalogExtractor.MissionEntry("egg_shipment", "EGG SHIPMENT", "Ship an egg"),
            new MissionCatalogExtractor.MissionEntry("broken_row", null, null)
        ], "1.35.8");

        GameDataProvider.Validate("missions", doc.Json);
        var parsed = MissionCatalog.Parse(doc.Json);
        Assert.Equal(1, doc.Count);
        Assert.Single(parsed.Missions);
        Assert.Equal(["broken_row"], doc.Skipped);
        Assert.Equal("1.35.8", parsed.BinaryVersion);
        Assert.Equal("binary", parsed.Provenance["identity"].Origin);
        Assert.Equal("missiondata", parsed.Provenance["identity"].Locator);
    }

    [Fact]
    public void BuildEggs_KeepsNamelessRows_AndValidates() {
        var doc = GameDataDocBuilders.BuildEggs([
            new EggCatalogExtractor.EggEntry(0, "EDIBLE", 0.25),
            new EggCatalogExtractor.EggEntry(1, null, 1.25)
        ], "1.35.8");

        GameDataProvider.Validate("eggs", doc.Json);
        var parsed = EggCatalog.Parse(doc.Json);
        Assert.Equal(2, parsed.Eggs.Count);
        Assert.Null(parsed.Find(1)!.Name);
        Assert.Equal(0.25, parsed.Find(0)!.BaseValue);
    }

    [Fact]
    public void BuildVehicles_DropsNamelessRows_AndValidates() {
        var doc = GameDataDocBuilders.BuildVehicles([
            new VehicleCatalogExtractor.VehicleEntry(0, "TRIKE", 5000),
            new VehicleCatalogExtractor.VehicleEntry(1, null, 9999)
        ], "1.35.8");

        GameDataProvider.Validate("vehicles", doc.Json);
        var parsed = VehicleCatalog.Parse(doc.Json);
        Assert.Single(parsed.Vehicles);
        Assert.Equal(["1"], doc.Skipped);
        Assert.Equal(5000, parsed.Find(0)!.Capacity);
    }

    [Fact]
    public void BuildResearch_EmitsRowsAndSkipsUndecoded_AndValidates() {
        var doc = GameDataDocBuilders.BuildResearch([
            new ResearchCatalogExtractor.ResearchEntry("comfy_nests", "COMFORTABLE NESTS",
                "Increase egg laying rate by 10%", null, false, 50, 0, "eggLayingRateMult", false,
                ResearchCatalogExtractor.Combine.MulPlusOne, 0.1, null),
            new ResearchCatalogExtractor.ResearchEntry("broken", null, null, null, false, null, null, null, false,
                null, null, "unrecognized effect pattern")
        ], "1.35.8");

        GameDataProvider.Validate("research", doc.Json);
        Assert.Equal(1, doc.Count);
        string skip = Assert.Single(doc.Skipped);
        Assert.Contains("broken", skip);

        var parsed = EffectDataLoader.Parse(doc.Json);
        var row = Assert.Single(parsed.Rows);
        Assert.Equal(EffectTarget.EggLayingRate, row.Target);
        Assert.Equal(CombineMode.MulPlusOne, row.CombineMode);
        Assert.Equal(0.1, row.Magnitude);
        Assert.Equal(50, row.MaxLevel);
        Assert.False(row.Meta!["epic"].GetBoolean());
        Assert.Equal("COMFORTABLE NESTS", row.Meta["name"].GetString());
    }

    [Fact]
    public void BuildHabs_EmitsCapacityRows_AndValidates() {
        var doc = GameDataDocBuilders.BuildHabs([
            new HabCatalogExtractor.HabEntry(0, "COOP", 250),
            new HabCatalogExtractor.HabEntry(14, "HAB 10,000", 10_000_000),
            new HabCatalogExtractor.HabEntry(3, null, 2000)
        ], "1.35.8");

        GameDataProvider.Validate("habs", doc.Json);
        Assert.Equal(2, doc.Count);
        Assert.Equal(["3"], doc.Skipped);

        var parsed = EffectDataLoader.Parse(doc.Json);
        Assert.Equal(2, parsed.Rows.Count);
        var coop = parsed.Rows[0];
        Assert.Equal("coop", coop.Id);
        Assert.Equal(EffectTarget.HabCapacity, coop.Target);
        Assert.Equal(CombineMode.Add, coop.CombineMode);
        Assert.Equal(250, coop.Magnitude);
        Assert.Equal(1, coop.MaxLevel);
        Assert.Equal(0, coop.Meta!["habId"].GetInt32());
        Assert.Equal("hab_10000", parsed.Rows[1].Id);
        Assert.Equal("HAB 10,000", parsed.Rows[1].Meta!["name"].GetString());
        Assert.Equal("binary", parsed.Provenance!["capacity"].Origin);
        Assert.Equal("derived", parsed.Provenance["id"].Origin);
    }

    [Fact]
    public void BuildDimensions_Validates() {
        var doc = GameDataDocBuilders.BuildDimensions(["bd-earnings", "bd-egg-value"], "1.35.8");

        GameDataProvider.Validate("dimensions", doc.Json);
        var parsed = DimensionCatalog.Parse(doc.Json);
        Assert.Equal(2, parsed.Dimensions.Count);
        Assert.True(parsed.Contains("bd-earnings"));
    }
}
