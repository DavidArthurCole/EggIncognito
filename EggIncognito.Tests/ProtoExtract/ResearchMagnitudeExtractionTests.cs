using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class ResearchMagnitudeExtractionTests {
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public void Additive_ihr_increments_present_in_researchdata_init(double inc) {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_researchdata.cpp");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(StaticInitDoubleExtractorTests.Has(r.Values, inc), $"research increment {inc} not in binary init");
    }
}
