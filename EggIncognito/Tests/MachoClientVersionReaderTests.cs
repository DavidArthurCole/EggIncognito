using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class MachoClientVersionReaderTests {
    [Fact]
    public void Pick_SelectsInRangeNearestToPrev() {
        var cands = new Dictionary<int, int> { [19] = 37, [71] = 4, [72] = 7, [130] = 2 };
        Assert.Equal(71, MachoClientVersionReader.Pick(cands, 71));
    }

    [Fact]
    public void Pick_BumpsToPrevPlusOne() {
        var cands = new Dictionary<int, int> { [19] = 37, [72] = 7 };
        Assert.Equal(72, MachoClientVersionReader.Pick(cands, 71));
    }

    [Fact]
    public void Pick_NearerCandidateWinsOverHigherSiteCount() {
        var cands = new Dictionary<int, int> { [73] = 9, [74] = 9 };
        Assert.Equal(73, MachoClientVersionReader.Pick(cands, 72));
    }

    [Fact]
    public void Pick_SameDistance_TieBreaksByDescendingSiteCount() {
        var cands = new Dictionary<int, int> { [72] = 2, [73] = 99 };
        Assert.Equal(72, MachoClientVersionReader.Pick(cands, 72));
    }

    [Fact]
    public void Pick_NoInRangeCandidate_ReturnsNull() {
        var cands = new Dictionary<int, int> { [19] = 37, [200] = 5 };
        Assert.Null(MachoClientVersionReader.Pick(cands, 71));
    }

    [Fact]
    public void Pick_PrevNull_ReturnsNull() {
        var cands = new Dictionary<int, int> { [72] = 7 };
        Assert.Null(MachoClientVersionReader.Pick(cands, null));
    }


    [Fact]
    public void Candidates_RequiresThreeDistinctSites() {
        var insns = new List<Arm64Insn>();
        ulong addr = 0x1000;
        for (int k = 0; k < 3; k++) {
            insns.Add(new Arm64Insn(addr, Arm64Op.Movz, 0, 0, 72));
            addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Str, 0, 1, 0x110));
            addr += 4;
        }

        for (int k = 0; k < 2; k++) {
            insns.Add(new Arm64Insn(addr, Arm64Op.Movz, 2, 0, 99));
            addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Str, 2, 3, 0x20));
            addr += 4;
        }

        var cands = MachoClientVersionReader.Candidates(insns);
        Assert.True(cands.ContainsKey(72));
        Assert.Equal(3, cands[72]);
        Assert.False(cands.ContainsKey(99));
    }

    [Fact]
    public void Candidates_MovkOverlayResolvesValue() {
        var insns = new List<Arm64Insn>();
        ulong addr = 0x2000;
        for (int k = 0; k < 3; k++) {
            insns.Add(new Arm64Insn(addr, Arm64Op.Movz, 0, 0, 0));
            addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Movk, 0, 0, 72));
            addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Str, 0, 1, 0x40));
            addr += 4;
        }

        var cands = MachoClientVersionReader.Candidates(insns);
        Assert.True(cands.ContainsKey(72));
    }
}
