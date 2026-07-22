using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class StaticInitDoubleExtractorTests {
    internal static bool Has(IReadOnlyList<double> values, double target)
        => values.Any(v => Math.Abs(v - target) < 1e-9);

    [Fact]
    public void Composes_movz_movk_double_bits() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_boostmanager");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(Has(r.Values, 10));
        Assert.True(Has(r.Values, 100));
    }

    [Fact]
    public void Composes_orr_mantissa_double_bits() {
        if (!BinaryFixture.TryLoad(out var bin)) return;

        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_researchdata.cpp");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(Has(r.Values, 0.7), "orr+movk lane-clear composition must yield 0.7");
    }
}
