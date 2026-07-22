using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class BoostMagnitudeExtractionTests {
    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Ihr_mults_present_in_boostmanager_init(double mult) {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_boostmanager");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(StaticInitDoubleExtractorTests.Has(r.Values, mult), $"IHR mult {mult} not in binary init");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(50)]
    public void Beacon_multipliers_present(double m) {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_boostmanager");
        Assert.True(StaticInitDoubleExtractorTests.Has(r.Values, m), $"beacon multiplier {m} not in binary init");
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1200)]
    [InlineData(1800)]
    [InlineData(3600)]
    [InlineData(7200)]
    [InlineData(14400)]
    public void Durations_present(double sec) {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_boostmanager");
        Assert.True(StaticInitDoubleExtractorTests.Has(r.Values, sec), $"duration {sec} not in binary init");
    }
}
