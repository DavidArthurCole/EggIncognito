using EggIncognito.GameData;
using EggIncognito.Services.ProtoExtract;
using EggIncognito.Tests.ProtoExtract;

namespace EggIncognito.Tests.GameData;

public class MissionCatalogParityTests {
    [Fact]
    public void Committed_catalog_loads_with_expected_shape() {
        var cat = MissionCatalog.Load();

        Assert.Equal(48, cat.Missions.Count);
        Assert.Equal("TWO HUNDRED", cat.Find("two_hundred")!.DisplayName);
        Assert.Equal("Hatch 200 chickens", cat.Find("two_hundred")!.Goal);
        Assert.Equal("DRONE PRACTICE", cat.Find("drone_takedown_five")!.DisplayName);
        Assert.Null(cat.Find("research_all")!.DisplayName);
        Assert.StartsWith("Earn " + (char)0x1b + "b[s8]500", cat.Find("earn_500s")!.Goal);
        Assert.Equal("binary", cat.Provenance["identity"].Origin);
        Assert.Equal("missiondata", cat.Provenance["goal"].Locator);
    }

    [Fact]
    public void Committed_catalog_matches_extractor_output() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var extracted = MissionCatalogExtractor.Extract(bin);
        Assert.True(extracted.Ok, extracted.Diagnostics);

        var committed = MissionCatalog.Load();
        Assert.Equal(extracted.Entries.Count, committed.Missions.Count);
        foreach (var e in extracted.Entries) {
            var c = committed.Find(e.Id);
            Assert.NotNull(c);
            Assert.Equal(e.DisplayName, c!.DisplayName);
            Assert.Equal(e.Goal, c.Goal);
        }
    }
}
