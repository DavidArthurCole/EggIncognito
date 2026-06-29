using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class MachoArm64DisassemblerTests
{
    [Fact]
    public void Analyze_ResolvesAdrpAddLdr_ToFloatConstants_AndBl()
    {
        // Fake binary: code at file 0 (vmaddr 0x1000), slide 0x1000. A f32 at file 0x80 (va 0x1080), a f64 at
        // file 0x88 (va 0x1088). adrp x0 resolves to page 0x1000; add reaches 0x1080 / 0x1088.
        const ulong codeVa = 0x1000;
        var bin = new byte[0x100];
        BitConverter.GetBytes(5.5f).CopyTo(bin, 0x80);
        BitConverter.GetBytes(-0.5d).CopyTo(bin, 0x88);

        var words = new List<uint>
        {
            Adrp(0, codeVa, 0x1080),   // x0 = page of 0x1080 = 0x1000
            Add(0, 0, 0x80),           // x0 = 0x1080
            LdrS(0, 0, 0),             // s0 = f32 @ 0x1080 = 5.5
            Adrp(1, codeVa + 12, 0x1088),
            Add(1, 1, 0x88),
            LdrD(2, 1, 0),             // d2 = f64 @ 0x1088 = -0.5
            Bl(codeVa + 24, 0x2000),
        };
        var code = words.SelectMany(BitConverter.GetBytes).ToArray();
        code.CopyTo(bin, 0);

        var res = MachoArm64Disassembler.Analyze(bin, codeVa, codeVa + (ulong)code.Length, textVmAddr: codeVa, textFileOff: 0);

        Assert.Contains(res.Floats, f => !f.IsF64 && Math.Abs(f.Value - 5.5) < 0.001);
        Assert.Contains(res.Floats, f => f.IsF64 && Math.Abs(f.Value - (-0.5)) < 0.001);
        Assert.Contains(res.CallTargets, t => t == 0x2000);
    }

    [Fact]
    public void Analyze_ReadsVectorImmediates_FmovBroadcast_AndMoviZero()
    {
        const ulong codeVa = 0x1000;
        // fmov v0.4s, #1.0 (0x4F03F600) broadcasts 1.0 across the lanes; movi v0.4s, #0 (0x4F000400) zeroes.
        var words = new uint[] { 0x4F03F600u, 0x4F000400u };
        var code = words.SelectMany(BitConverter.GetBytes).ToArray();

        var res = MachoArm64Disassembler.Analyze(code, codeVa, codeVa + (ulong)code.Length, textVmAddr: codeVa, textFileOff: 0);

        Assert.Contains(res.Floats, f => Math.Abs(f.Value - 1.0) < 0.001);
        Assert.Contains(res.Floats, f => f.Value == 0.0);
    }

    // adrp Xd, <page of target>: imm21 = (targetPage - pcPage) >> 12, split immlo(2)+immhi(19).
    static uint Adrp(int rd, ulong pc, ulong target)
    {
        long pageDelta = ((long)(target & ~0xFFFUL) - (long)(pc & ~0xFFFUL)) >> 12;
        uint immlo = (uint)(pageDelta & 0x3), immhi = (uint)((pageDelta >> 2) & 0x7FFFF);
        return 0x90000000u | (immlo << 29) | (immhi << 5) | (uint)(rd & 0x1F);
    }
    static uint Add(int rd, int rn, uint imm) => 0x91000000u | ((imm & 0xFFF) << 10) | ((uint)(rn & 0x1F) << 5) | (uint)(rd & 0x1F);
    static uint LdrS(int rt, int rn, uint imm) => 0xBD400000u | (((imm / 4) & 0xFFF) << 10) | ((uint)(rn & 0x1F) << 5) | (uint)(rt & 0x1F);
    static uint LdrD(int rt, int rn, uint imm) => 0xFD400000u | (((imm / 8) & 0xFFF) << 10) | ((uint)(rn & 0x1F) << 5) | (uint)(rt & 0x1F);
    static uint Bl(ulong pc, ulong target) => 0x94000000u | (uint)((((long)target - (long)pc) >> 2) & 0x03FFFFFF);
}
