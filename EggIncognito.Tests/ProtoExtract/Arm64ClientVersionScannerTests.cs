using System.Collections.Generic;
using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class Arm64ClientVersionScannerTests
{
   
    private static uint Movz(int wd, int imm16) => 0x52800000u | ((uint)(imm16 & 0xFFFF) << 5) | (uint)(wd & 0x1F);
    private static uint StrW(int wt, int xn, int imm12) =>
        0xB9000000u | ((uint)(imm12 & 0xFFF) << 10) | ((uint)(xn & 0x1F) << 5) | (uint)(wt & 0x1F);

    private static byte[] Asm(IEnumerable<uint> words)
    {
        var list = new List<uint>(words);
        var b = new byte[list.Count * 4];
        for (int i = 0; i < list.Count; i++)
            for (int k = 0; k < 4; k++) b[i * 4 + k] = (byte)(list[i] >> (8 * k));
        return b;
    }

    [Fact]
    public void Scan_PicksInRangeCandidate_PrevAnchored()
    {
       
       
        var words = new List<uint>();
        for (int s = 0; s < 3; s++) { words.Add(Movz(0, 72)); words.Add(StrW(0, 1, 0x11)); words.Add(0xD503201F); }
        for (int s = 0; s < 4; s++) { words.Add(Movz(2, 19)); words.Add(StrW(2, 1, 0x22)); words.Add(0xD503201F); }
        var r = Arm64ClientVersionScanner.Scan(Asm(words), prevClientVersion: 71);
        Assert.Equal(72, r.ClientVersion);
        Assert.Contains(72, r.Candidates);
        Assert.Contains(19, r.Candidates);
    }

    [Fact]
    public void Scan_NoInRangeCandidate_ReturnsNull()
    {
        var words = new List<uint>();
        for (int s = 0; s < 3; s++) { words.Add(Movz(0, 19)); words.Add(StrW(0, 1, 0x11)); }
        var r = Arm64ClientVersionScanner.Scan(Asm(words), prevClientVersion: 71);
        Assert.Null(r.ClientVersion);
    }

    [Fact]
    public void Scan_FewerThanThreeSites_NotACandidate()
    {
        var words = new List<uint>();
        for (int s = 0; s < 2; s++) { words.Add(Movz(0, 72)); words.Add(StrW(0, 1, 0x11)); }
        var r = Arm64ClientVersionScanner.Scan(Asm(words), prevClientVersion: 71);
        Assert.DoesNotContain(72, r.Candidates);
    }
}
