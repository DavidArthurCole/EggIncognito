using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class FunctionConstantExtractorTests
{
    [Fact]
    public void ResolveCallName_PicksNearestSymbolAtOrBelowTarget()
    {
        var syms = new List<MachoSymbols.Symbol>
        {
            new("__ZN3FooC1Ev", 0x1000, 0, 1),
            new("__ZN3Bar6updateEv", 0x2000, 0, 1),
        };
        Assert.Equal("__ZN3Bar6updateEv", FunctionConstantExtractor.ResolveCallName(syms, 0x2010));
        Assert.Equal("__ZN3FooC1Ev", FunctionConstantExtractor.ResolveCallName(syms, 0x1004));
        Assert.StartsWith("0x", FunctionConstantExtractor.ResolveCallName(syms, 0x10)); // below all -> hex
    }

    [Fact]
    public void Extract_RealBinary_SiloConstants_Regression()
    {
        var bin = TestBinary();
        if (bin is null) return; // fixture absent (CI): the synthetic + manual cover the path

        // The silo layout function carries the disassembled constants 5.5 and -0.5 (FarmLayout.SiloPos Z values).
        var res = FunctionConstantExtractor.Extract(bin, ["updateSilo", "FarmScene9updateSilo", "FarmScene10updateSilos"]);
        Assert.True(res.Ok, res.Diagnostics);
        Assert.Contains(res.Floats, f => Math.Abs(f - 5.5) < 0.01);
        Assert.Contains(res.Floats, f => Math.Abs(f - (-0.5)) < 0.01);
    }

    // Optional local egginc Mach-O fixture; absent on CI. Same pattern as ShellCatalogTests.ConfigJson().
    private static byte[]? TestBinary()
    {
        foreach (var rel in new[] { "../../../../captures/egginc", "../../../../../captures/egginc" })
        {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }
        return null;
    }
}
