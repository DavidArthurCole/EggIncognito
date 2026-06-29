using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

public class Elf64Tests
{
    // Builds a tiny but structurally valid ELF64 with one ".text" section so the reader can be unit-tested
    // without a real 24MB libegginc.so.
    private static byte[] MiniElf(ulong textVAddr, long textOffset, long textSize)
    {
        // Layout: [ehdr 64][shstrtab bytes][shdr0 null][shdr1 .text][shdr2 .shstrtab]
        var shstrtab = System.Text.Encoding.ASCII.GetBytes("\0.text\0.shstrtab\0");
        int textNameOff = 1;          // ".text" starts at index 1
        int strtabNameOff = 7;        // ".shstrtab" starts at index 7
        int ehdr = 64, shentsize = 64;
        int shstrtabFileOff = ehdr;
        int shoff = shstrtabFileOff + shstrtab.Length;
        int shnum = 3, shstrndx = 2;

        var buf = new byte[shoff + shnum * shentsize];
        buf[0] = 0x7F; buf[1] = (byte)'E'; buf[2] = (byte)'L'; buf[3] = (byte)'F';
        buf[4] = 2; // EI_CLASS = ELFCLASS64
        buf[5] = 1; // EI_DATA = little-endian
        void U16(int p, ushort v) { buf[p] = (byte)v; buf[p + 1] = (byte)(v >> 8); }
        void U32(int p, uint v) { for (int i = 0; i < 4; i++) buf[p + i] = (byte)(v >> (8 * i)); }
        void U64(int p, ulong v) { for (int i = 0; i < 8; i++) buf[p + i] = (byte)(v >> (8 * i)); }
        U64(0x28, (ulong)shoff);
        U16(0x3A, (ushort)shentsize);
        U16(0x3C, (ushort)shnum);
        U16(0x3E, (ushort)shstrndx);
        System.Array.Copy(shstrtab, 0, buf, shstrtabFileOff, shstrtab.Length);

        int s1 = shoff + 1 * shentsize;
        U32(s1 + 0x00, (uint)textNameOff);
        U64(s1 + 0x10, textVAddr);
        U64(s1 + 0x18, (ulong)textOffset);
        U64(s1 + 0x20, (ulong)textSize);

        int s2 = shoff + 2 * shentsize;
        U32(s2 + 0x00, (uint)strtabNameOff);
        U64(s2 + 0x18, (ulong)shstrtabFileOff);
        U64(s2 + 0x20, (ulong)shstrtab.Length);
        return buf;
    }

    [Fact]
    public void FindSection_Text_ReturnsAddrOffsetSize()
    {
        var elf = MiniElf(0x1000, 0x400, 0x2000);
        var s = Elf64.FindSection(elf, ".text");
        Assert.NotNull(s);
        Assert.Equal(0x1000ul, s!.VAddr);
        Assert.Equal(0x400, s.FileOffset);
        Assert.Equal(0x2000, s.Size);
    }

    [Fact]
    public void FindSection_Missing_ReturnsNull()
    {
        var elf = MiniElf(0x1000, 0x400, 0x2000);
        Assert.Null(Elf64.FindSection(elf, ".rodata"));
    }

    [Fact]
    public void FindSection_NotElf_ReturnsNull()
    {
        Assert.Null(Elf64.FindSection([1, 2, 3, 4, 5, 6, 7, 8], ".text"));
    }
}
