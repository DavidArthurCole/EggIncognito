using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests;

public class MachoTextTests
{
    // Build a minimal thin ARM64 Mach-O with one LC_SEGMENT_64 "__TEXT" containing one "__text" section.
    private static byte[] MinimalMacho(uint textFileOff, uint textSize, ulong textVmAddr, out byte[] textBytes)
    {
        textBytes = new byte[textSize];
        for (int i = 0; i < textSize; i++) textBytes[i] = (byte)(i & 0xFF);

        int headerEnd = 32 + 72 + 80; // header + LC_SEGMENT_64 cmd hdr + 1 section
        int total = (int)Math.Max(headerEnd, textFileOff + textSize);
        var b = new byte[total];

        void U32(int off, uint v) { b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24); }
        void U64(int off, ulong v) { for (int k = 0; k < 8; k++) b[off + k] = (byte)(v >> (k * 8)); }
        void Str16(int off, string s) { var sb = System.Text.Encoding.ASCII.GetBytes(s); Array.Copy(sb, 0, b, off, sb.Length); }

        U32(0, 0xFEEDFACF); // magic 64-bit
        U32(4, 0x0100000C); // cputype ARM64
        U32(8, 0); // cpusubtype
        U32(12, 2); // filetype MH_EXECUTE
        U32(16, 1); // ncmds
        U32(20, 72 + 80); // sizeofcmds
        U32(24, 0); // flags
        U32(28, 0); // reserved

        int lc = 32;
        U32(lc, 0x19); // LC_SEGMENT_64
        U32(lc + 4, 72 + 80); // cmdsize
        Str16(lc + 8, "__TEXT"); // segname (16 bytes)
        U64(lc + 24, 0); // vmaddr (seg)
        U64(lc + 32, 0); // vmsize
        U64(lc + 40, 0); // fileoff
        U64(lc + 48, 0); // filesize
        U32(lc + 56, 0); U32(lc + 60, 0); // maxprot/initprot
        U32(lc + 64, 1); // nsects
        U32(lc + 68, 0); // flags

        int sec = lc + 72;
        Str16(sec, "__text"); // sectname (16)
        Str16(sec + 16, "__TEXT"); // segname (16)
        U64(sec + 32, textVmAddr); // addr
        U64(sec + 40, textSize); // size
        U32(sec + 48, textFileOff); // offset

        Array.Copy(textBytes, 0, b, (int)textFileOff, (int)textSize);
        return b;
    }

    [Fact]
    public void TryFindText_ThinArm64_ReturnsSection()
    {
        var macho = MinimalMacho(0x200, 0x40, 0x100000000, out var expected);
        Assert.True(MachoText.TryFindText(macho, out int off, out int size, out ulong vm));
        Assert.Equal(0x200, off);
        Assert.Equal(0x40, size);
        Assert.Equal(0x100000000ul, vm);
        Assert.Equal(expected, macho.AsSpan(off, size).ToArray());
    }

    [Fact]
    public void TryFindText_NotMacho_ReturnsFalse()
    {
        Assert.False(MachoText.TryFindText(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, out _, out _, out _));
    }
}
