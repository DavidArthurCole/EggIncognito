using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class StaticInitDoubleExtractorTests
{
    internal static byte[]? Bin()
    {
        foreach (var rel in new[] { "../../../../captures/ipas", "../../../../../captures/ipas" })
        {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (!Directory.Exists(full)) continue;
            var store = new SymbolizedBinaryStore(full);
            foreach (var v in new[] { "1.35.6", "1.35.7", "1.35.5" })
            {
                var r = store.Get(v);
                if (r.Ok && r.Bytes is not null) return r.Bytes;
            }
        }
        return null;
    }

    internal static bool Has(IReadOnlyList<double> values, double target)
        => values.Any(v => Math.Abs(v - target) < 1e-9);

    [Fact]
    public void Composes_movz_movk_double_bits()
    {
        var bin = Bin();
        if (bin is null) return;

        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_boostmanager");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(Has(r.Values, 10));
        Assert.True(Has(r.Values, 100));
    }

    [Fact]
    public void Composes_orr_mantissa_double_bits()
    {
        var bin = Bin();
        if (bin is null) return;

        var r = StaticInitDoubleExtractor.Extract(bin, "__GLOBAL__sub_I_researchdata.cpp");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.True(Has(r.Values, 0.7), "orr+movk lane-clear composition must yield 0.7");
    }
}
