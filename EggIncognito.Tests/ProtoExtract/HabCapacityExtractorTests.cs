using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class HabCapacityExtractorTests {
    [Fact]
    public void Extracts_full_hab_capacity_sequence_from_binary() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = HabCapacityExtractor.Extract(bin);
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(
            [250L, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000,
             1_000_000, 2_000_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000, 600_000_000],
            r.Capacities);
    }
}
