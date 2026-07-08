using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class Arm64DecoderTests
{
    // Assemble 32-bit instruction words little-endian into a byte stream.
    private static byte[] Words(params uint[] words)
    {
        var b = new byte[words.Length * 4];
        for (int i = 0; i < words.Length; i++)
        {
            b[i * 4 + 0] = (byte)(words[i] & 0xFF);
            b[i * 4 + 1] = (byte)((words[i] >> 8) & 0xFF);
            b[i * 4 + 2] = (byte)((words[i] >> 16) & 0xFF);
            b[i * 4 + 3] = (byte)((words[i] >> 24) & 0xFF);
        }
        return b;
    }

    // MOVZ Wd, #imm (32-bit): base 0x52800000 | (hw<<21) | (imm16<<5) | Rd
    private static uint Movz(int rd, int imm16, int hw = 0) =>
        0x52800000u | ((uint)hw << 21) | ((uint)imm16 << 5) | (uint)rd;

    // MOVK Wd, #imm, LSL #(hw*16): base 0x72800000
    private static uint Movk(int rd, int imm16, int hw) =>
        0x72800000u | ((uint)hw << 21) | ((uint)imm16 << 5) | (uint)rd;

    // STR Wt, [Xn, #off] unsigned offset (32-bit): base 0xB9000000 | (imm12<<10) | (Rn<<5) | Rt; imm12 = off/4
    private static uint Str(int rt, int rn, int byteOff) =>
        0xB9000000u | (((uint)byteOff / 4) << 10) | ((uint)rn << 5) | (uint)rt;

    [Fact]
    public void Decode_Movz_DecodesRegAndImm()
    {
        var insns = Arm64Decoder.Decode(Words(Movz(0, 72)), 0x1000);
        var movz = Assert.Single(insns, i => i.Op == Arm64Op.Movz);
        Assert.Equal(0x1000u, movz.Address);
        Assert.Equal(0, movz.Rd);
        Assert.Equal(72, movz.Imm);
        Assert.Equal(0, movz.Rn);
    }

    [Fact]
    public void Decode_Movk_CarriesHwShift()
    {
        var insns = Arm64Decoder.Decode(Words(Movk(3, 0xABCD, 1)), 0);
        var movk = Assert.Single(insns, i => i.Op == Arm64Op.Movk);
        Assert.Equal(3, movk.Rd);
        Assert.Equal(0xABCD, movk.Imm);
        Assert.Equal(1, movk.Rn);
    }

    [Fact]
    public void Decode_Str_DecodesRtBaseOffset()
    {
        var insns = Arm64Decoder.Decode(Words(Str(0, 1, 0x110)), 0x2000);
        var str = Assert.Single(insns, i => i.Op == Arm64Op.Str);
        Assert.Equal(0x2000u, str.Address);
        Assert.Equal(0, str.Rd);
        Assert.Equal(1, str.Rn);
        Assert.Equal(0x110, str.Imm);
    }

    [Fact]
    public void Decode_UnknownWord_SkippedNotEmitted()
    {
        var insns = Arm64Decoder.Decode(Words(0xD503201F, Movz(2, 5)), 0);
        Assert.Single(insns);
        Assert.Equal(Arm64Op.Movz, insns[0].Op);
    }

    [Fact]
    public void Decode_AddressesAdvanceByFour()
    {
        var insns = Arm64Decoder.Decode(Words(Movz(0, 1), Str(0, 1, 4)), 0x100);
        Assert.Equal(0x100u, insns[0].Address);
        Assert.Equal(0x104u, insns[1].Address);
    }
}
