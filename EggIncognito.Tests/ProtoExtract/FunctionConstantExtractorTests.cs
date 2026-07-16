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
        Assert.StartsWith("0x", FunctionConstantExtractor.ResolveCallName(syms, 0x10));
    }

    [Fact]
    public void Extract_RealBinary_SiloConstants_Regression()
    {
        var bin = TestBinary();
        if (bin is null) return;

       
        var res = FunctionConstantExtractor.Extract(bin, ["FarmScene10updateSilo"]);
        Assert.True(res.Ok, res.Diagnostics);
        Assert.Contains(res.Floats, f => Math.Abs(f - 5.5) < 0.01);
        Assert.Contains(res.Floats, f => Math.Abs(f - (-0.5)) < 0.01);
    }

    [Fact]
    public void Extract_RealBinary_GalaxyParticle_HasConstants()
    {
        var bin = TestBinary();
        if (bin is null) return;

       
       
       
        var onBirth = FunctionConstantExtractor.Extract(bin, ["GalaxyParticle7onBirth"]);
        var update = FunctionConstantExtractor.Extract(bin, ["GalaxyParticle6update"]);
        Assert.True(onBirth.Ok, onBirth.Diagnostics);
        Assert.True(update.Ok, update.Diagnostics);
        Assert.True(onBirth.Floats.Count + update.Floats.Count > 0, "no orbit constants recovered");
    }

   
    private static byte[]? TestBinary()
    {
        foreach (var rel in new[] { "../../../../captures/egginc", "../../../../../captures/egginc", "../../../../EggIncognito/captures/egginc" })
        {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }
        return null;
    }
}
