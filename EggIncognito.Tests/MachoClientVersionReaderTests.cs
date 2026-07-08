using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class MachoClientVersionReaderTests
{
    // Pick is the prev-anchor selection over a candidate->siteCount map.
    [Fact]
    public void Pick_SelectsInRangeNearestToPrev()
    {
        var cands = new Dictionary<int, int> { [19] = 37, [71] = 4, [72] = 7, [130] = 2 };
        Assert.Equal(71, MachoClientVersionReader.Pick(cands, 71));
    }

    [Fact]
    public void Pick_BumpsToPrevPlusOne()
    {
        var cands = new Dictionary<int, int> { [19] = 37, [72] = 7 };
        Assert.Equal(72, MachoClientVersionReader.Pick(cands, 71)); // 71 absent, 72 in {71,72,73}
    }

    [Fact]
    public void Pick_NearerCandidateWinsOverHigherSiteCount()
    {
        // prev=72 -> range {72,73,74}. 73 is nearer than 74, so 73 wins despite equal site counts.
        var cands = new Dictionary<int, int> { [73] = 9, [74] = 9 };
        Assert.Equal(73, MachoClientVersionReader.Pick(cands, 72));
    }

    [Fact]
    public void Pick_SameDistance_TieBreaksByDescendingSiteCount()
    {
        var cands = new Dictionary<int, int> { [72] = 2, [73] = 99 };
        Assert.Equal(72, MachoClientVersionReader.Pick(cands, 72)); // 72 nearest, chosen over higher-count 73
    }

    [Fact]
    public void Pick_NoInRangeCandidate_ReturnsNull()
    {
        var cands = new Dictionary<int, int> { [19] = 37, [200] = 5 };
        Assert.Null(MachoClientVersionReader.Pick(cands, 71));
    }

    [Fact]
    public void Pick_PrevNull_ReturnsNull()
    {
        var cands = new Dictionary<int, int> { [72] = 7 };
        Assert.Null(MachoClientVersionReader.Pick(cands, null));
    }

    // Candidates: a value written to the same (base,offset) from >=3 sites is a candidate; <3 is dropped.
    [Fact]
    public void Candidates_RequiresThreeDistinctSites()
    {
        // Build synthetic insns: three MOVZ W0,#72 / STR W0,[X1,#0x110] at distinct addresses -> candidate.
        var insns = new List<Arm64Insn>();
        ulong addr = 0x1000;
        for (int k = 0; k < 3; k++)
        {
            insns.Add(new Arm64Insn(addr, Arm64Op.Movz, 0, 0, 72)); addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Str, 0, 1, 0x110)); addr += 4;
        }
        // A value with only 2 sites must NOT appear.
        for (int k = 0; k < 2; k++)
        {
            insns.Add(new Arm64Insn(addr, Arm64Op.Movz, 2, 0, 99)); addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Str, 2, 3, 0x20)); addr += 4;
        }
        var cands = MachoClientVersionReader.Candidates(insns);
        Assert.True(cands.ContainsKey(72));
        Assert.Equal(3, cands[72]);
        Assert.False(cands.ContainsKey(99));
    }

    [Fact]
    public void Candidates_MovkOverlayResolvesValue()
    {
        // MOVZ W0,#0  then MOVK W0,#72  -> 72, stored 3x.
        var insns = new List<Arm64Insn>();
        ulong addr = 0x2000;
        for (int k = 0; k < 3; k++)
        {
            insns.Add(new Arm64Insn(addr, Arm64Op.Movz, 0, 0, 0)); addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Movk, 0, 0, 72)); addr += 4;
            insns.Add(new Arm64Insn(addr, Arm64Op.Str, 0, 1, 0x40)); addr += 4;
        }
        var cands = MachoClientVersionReader.Candidates(insns);
        Assert.True(cands.ContainsKey(72));
    }
}
