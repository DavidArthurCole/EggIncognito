using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class MissionCatalogExtractorTests {
    [Fact]
    public void Extracts_mission_ids_names_and_goals() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = MissionCatalogExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(r.Entries.Count >= 40, r.Diagnostics);

        var first = r.Entries[0];
        Assert.Equal("two_hundred", first.Id);
        Assert.Equal("TWO HUNDRED", first.DisplayName);
        Assert.Equal("Hatch 200 chickens", first.Goal);

        var drones = r.Entries.Single(e => e.Id == "drone_takedown_five");
        Assert.Equal("DRONE PRACTICE", drones.DisplayName);
        Assert.Equal("Take down 5 drones.", drones.Goal);

        Assert.Contains(r.Entries, e => e.Id == "research_all");
        Assert.Equal(r.Entries.Count, r.Entries.Select(e => e.Id).Distinct().Count());
    }
}
