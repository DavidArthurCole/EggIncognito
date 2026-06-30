using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

// Arm64Signature emits a frida byte-scan pattern from a function prologue with pc-relative displacement bytes
// wildcarded, so a relocated-but-same-shape prologue matches across builds. Covers: a fixed instruction stays
// literal, a bl/adrp gets its low 3 bytes masked (opcode byte kept), short functions are rejected.
public class Arm64SignatureTests
{
    private static uint Bl(long pc, long target) => 0x94000000u | (uint)(((target - pc) >> 2) & 0x03FFFFFF);
    private static uint MovZ(int rd, uint imm16) => 0xD2800000u | ((imm16 & 0xFFFF) << 5) | (uint)(rd & 0x1F);
    private static uint Ret() => 0xD65F03C0u;
    private static byte[] Words(params uint[] ws) => ws.SelectMany(BitConverter.GetBytes).ToArray();

    [Fact]
    public void Build_MasksPcRelativeWords_KeepsFixedWords()
    {
        var vm = SyntheticMacho.TextVm;
        // movz (fixed) ; bl (pc-rel, masked) ; movz (fixed) ; ret
        var code = Words(MovZ(0, 0x1234), Bl((long)vm + 4, (long)vm + 0x400), MovZ(1, 0x5678), Ret());
        var bin = SyntheticMacho.Build(code, []);
        Assert.True(MachoText.TryFindText(bin, out var tfo, out _, out var tvm));

        var pat = Arm64Signature.Build(bin, vm, vm + (ulong)code.Length, tvm, tfo, 4);
        Assert.True(pat.Ok);
        Assert.Equal(4, pat.Instructions);
        Assert.Equal(1, pat.MaskedWords); // only the bl is pc-relative

        var words = pat.FridaPattern.Split(' ');
        Assert.Equal(16, words.Length); // 4 instructions x 4 bytes
        // the bl is words[4..8]: low 3 bytes masked, top (opcode) byte literal.
        Assert.Equal("??", words[4]);
        Assert.Equal("??", words[5]);
        Assert.Equal("??", words[6]);
        Assert.NotEqual("??", words[7]);
        // the first movz is fully literal.
        Assert.DoesNotContain("??", words[0]);
    }

    [Fact]
    public void Build_TooShort_NotOk()
    {
        var vm = SyntheticMacho.TextVm;
        var code = Words(Ret()); // one word
        var bin = SyntheticMacho.Build(code, []);
        Assert.True(MachoText.TryFindText(bin, out var tfo, out _, out var tvm));
        var pat = Arm64Signature.Build(bin, vm, vm + 4, tvm, tfo, 8);
        Assert.False(pat.Ok);
    }
}
