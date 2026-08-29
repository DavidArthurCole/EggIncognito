using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class Arm64SignatureTests {
    private static uint Bl(long pc, long target) => 0x94000000u | (uint)(((target - pc) >> 2) & 0x03FFFFFF);
    private static uint MovZ(int rd, uint imm16) => 0xD2800000u | ((imm16 & 0xFFFF) << 5) | (uint)(rd & 0x1F);
    private static uint Ret() => 0xD65F03C0u;
    private static byte[] Words(params uint[] ws) => [.. ws.SelectMany(BitConverter.GetBytes)];

    [Fact]
    public void Build_MasksPcRelativeWords_KeepsFixedWords() {
        const ulong vm = SyntheticMacho.TextVm;

        byte[] code = Words(MovZ(0, 0x1234), Bl((long)vm + 4, (long)vm + 0x400), MovZ(1, 0x5678), Ret());
        byte[] bin = SyntheticMacho.Build(code, []);
        Assert.True(MachoText.TryFindText(bin, out int tfo, out _, out ulong tvm));

        var pat = Arm64Signature.Build(bin, vm, vm + (ulong)code.Length, tvm, tfo, 4);
        Assert.True(pat.Ok);
        Assert.Equal(4, pat.Instructions);
        Assert.Equal(1, pat.MaskedWords);

        string[] words = pat.FridaPattern.Split(' ');
        Assert.Equal(16, words.Length);

        Assert.Equal("??", words[4]);
        Assert.Equal("??", words[5]);
        Assert.Equal("??", words[6]);
        Assert.NotEqual("??", words[7]);

        Assert.DoesNotContain("??", words[0]);
    }

    [Fact]
    public void Build_TooShort_NotOk() {
        const ulong vm = SyntheticMacho.TextVm;
        byte[] code = Words(Ret());
        byte[] bin = SyntheticMacho.Build(code, []);
        Assert.True(MachoText.TryFindText(bin, out int tfo, out _, out ulong tvm));
        var pat = Arm64Signature.Build(bin, vm, vm + 4, tvm, tfo, 8);
        Assert.False(pat.Ok);
    }
}
