using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class FunctionConstantExtractorTests {
    [Fact]
    public void ResolveCallName_PicksNearestSymbolAtOrBelowTarget() {
        var syms = new List<MachoSymbols.Symbol> {
            new("__ZN3FooC1Ev", 0x1000, 0, 1),
            new("__ZN3Bar6updateEv", 0x2000, 0, 1)
        };
        Assert.Equal("__ZN3Bar6updateEv", FunctionConstantExtractor.ResolveCallName(syms, 0x2010));
        Assert.Equal("__ZN3FooC1Ev", FunctionConstantExtractor.ResolveCallName(syms, 0x1004));
        Assert.StartsWith("0x", FunctionConstantExtractor.ResolveCallName(syms, 0x10));
    }
}
