using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class SymbolResolutionTests {
    private const string TrophyCaseNeedle = "FarmScene16updateTrophyCaseEP14GameController";
    private const ulong TrophyCaseVa = 0x10008bc0c;

    [Fact]
    public void TryFindFunc_PrefersTheParentOverItsLambdas() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var syms = MachoSymbols.Read(bin);
        Assert.True(MachoSymbols.TryFindFunc(syms, [TrophyCaseNeedle], out var fn));
        Assert.Equal(TrophyCaseVa, fn.Start);
        Assert.False(MachoSymbols.IsLocalEntity(fn.Name));
    }

    [Fact]
    public void TryFindFunc_StillReachesALambdaWhenTheNeedleAsksForOne() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var syms = MachoSymbols.Read(bin);
        if (!MachoSymbols.TryFindFunc(syms, [TrophyCaseNeedle, "$_1"], out var fn)) return;
        Assert.True(MachoSymbols.IsLocalEntity(fn.Name));
        Assert.NotEqual(TrophyCaseVa, fn.Start);
    }

    [Fact]
    public void IsLocalEntity_ClassifiesManglings() {
        Assert.True(MachoSymbols.IsLocalEntity("__ZZN9FarmScene16updateTrophyCaseEP14GameControllerENK3$_1clEv"));
        Assert.True(MachoSymbols.IsLocalEntity("_some_method_block_invoke"));
        Assert.False(MachoSymbols.IsLocalEntity("__ZN9FarmScene16updateTrophyCaseEP14GameController"));
        Assert.False(MachoSymbols.IsLocalEntity(""));
    }

    [Fact]
    public void Index_ResolvesAVaToItsContainingFunction() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var index = MachoSymbols.Index.Build(MachoSymbols.Read(bin));
        Assert.True(index.TryResolve(TrophyCaseVa + 8, out var fn, out ulong off));
        Assert.Equal(TrophyCaseVa, fn.Start);
        Assert.Equal(8ul, off);
        Assert.Contains("updateTrophyCase", fn.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void FieldWriteScanner_FindsFarmSceneWidthWriters() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var r = Arm64FieldWriteScanner.Scan(bin, 0x3d0, 0x3db, "FarmScene");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.NotEmpty(r.Writes);
        Assert.All(r.Writes, w => Assert.InRange(w.Offset, 0x3d0, 0x3db));
        Assert.All(r.Writes, w => Assert.Contains("FarmScene", w.Symbol, StringComparison.Ordinal));
        Assert.All(r.Writes, w => Assert.NotEqual("sp", w.BaseReg));
    }

    [Fact]
    public void FieldWriteScanner_RejectsAnEmptyBinary() {
        var r = Arm64FieldWriteScanner.Scan([], 0, 0x10);
        Assert.False(r.Ok);
        Assert.Empty(r.Writes);
    }
}
