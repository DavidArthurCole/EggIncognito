using System.Text;
using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class BinaryImageTests {
    private const ulong TextVAddr = 0x1000;
    private const ulong InitArrayVAddr = 0x2000;
    private const ulong FuncValue = 0x1000;

    private static byte[] BuildElf() {
        byte[] shstrtab = Encoding.ASCII.GetBytes("\0.text\0.symtab\0.strtab\0.shstrtab\0.init_array\0");
        const int nameText = 1, nameSymtab = 7, nameStrtab = 15, nameShstrtab = 23, nameInitArray = 33;

        byte[] strtab = Encoding.ASCII.GetBytes("\0myfunc\0");
        const int symNameOff = 1;

        const int ehdr = 64, phentsize = 56, shentsize = 64;
        int phoff = ehdr;
        int textOff = phoff + phentsize;
        const int textSize = 8;
        int symtabOff = textOff + textSize;
        const int symtabSize = 48;
        int strtabOff = symtabOff + symtabSize;
        int strtabSize = strtab.Length;
        int shstrtabOff = strtabOff + strtabSize;
        int shstrtabSize = shstrtab.Length;
        int initArrayOff = Align8(shstrtabOff + shstrtabSize);
        const int initArraySize = 16;
        int shoff = Align8(initArrayOff + initArraySize);
        const int shnum = 6, shstrndx = 4;

        byte[] buf = new byte[shoff + shnum * shentsize];

        void U16(int p, ushort v) {
            buf[p] = (byte)v;
            buf[p + 1] = (byte)(v >> 8);
        }

        void U32(int p, uint v) {
            for (int i = 0; i < 4; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        void U64(int p, ulong v) {
            for (int i = 0; i < 8; i++) buf[p + i] = (byte)(v >> (8 * i));
        }

        buf[0] = 0x7F;
        buf[1] = (byte)'E';
        buf[2] = (byte)'L';
        buf[3] = (byte)'F';
        buf[4] = 2;
        buf[5] = 1;
        U16(0x12, 0xB7);
        U64(0x20, (ulong)phoff);
        U64(0x28, (ulong)shoff);
        U16(0x36, phentsize);
        U16(0x38, 1);
        U16(0x3A, shentsize);
        U16(0x3C, shnum);
        U16(0x3E, shstrndx);

        U32(phoff + 0x00, 1);
        U64(phoff + 0x08, (ulong)textOff);
        U64(phoff + 0x10, TextVAddr);
        U64(phoff + 0x20, textSize);
        U64(phoff + 0x28, textSize);

        Array.Copy(strtab, 0, buf, strtabOff, strtabSize);
        Array.Copy(shstrtab, 0, buf, shstrtabOff, shstrtabSize);

        int sym1 = symtabOff + 24;
        U32(sym1 + 0x00, symNameOff);
        buf[sym1 + 4] = 0x12;
        U16(sym1 + 6, 1);
        U64(sym1 + 8, FuncValue);
        U64(sym1 + 16, 0x40);

        void WriteShdr(int idx, uint name, uint type, ulong flags, ulong addr, ulong off, ulong sz, uint link,
            ulong entsize) {
            int h = shoff + idx * shentsize;
            U32(h + 0x00, name);
            U32(h + 0x04, type);
            U64(h + 0x08, flags);
            U64(h + 0x10, addr);
            U64(h + 0x18, off);
            U64(h + 0x20, sz);
            U32(h + 0x28, link);
            U64(h + 0x38, entsize);
        }

        WriteShdr(1, nameText, 1, 0x6, TextVAddr, (ulong)textOff, textSize, 0, 0);
        WriteShdr(2, nameSymtab, 2, 0, 0, (ulong)symtabOff, symtabSize, 3, 24);
        WriteShdr(3, nameStrtab, 3, 0, 0, (ulong)strtabOff, (ulong)strtabSize, 0, 0);
        WriteShdr(4, nameShstrtab, 3, 0, 0, (ulong)shstrtabOff, (ulong)shstrtabSize, 0, 0);
        WriteShdr(5, nameInitArray, 14, 0x3, InitArrayVAddr, (ulong)initArrayOff, initArraySize, 0, 8);

        return buf;
    }

    private static int Align8(int v) => (v + 7) & ~7;

    [Fact]
    public void ElfText_LocatesTextSection() {
        Assert.True(ElfText.TryFindText(BuildElf(), out int fileOff, out int size, out ulong vmAddr));
        Assert.Equal(0x1000ul, vmAddr);
        Assert.Equal(8, size);
        Assert.True(fileOff > 0);
    }

    [Fact]
    public void ElfSections_ReturnsAllocSectionsOnly() {
        var sections = ElfSections.Read(BuildElf());
        Assert.Contains(sections, s => s.Name == ".text" && s.VmAddr == TextVAddr && s.VmSize == 8);
        Assert.Contains(sections, s => s.Name == ".init_array");
        Assert.DoesNotContain(sections, s => s.Name == ".symtab");
    }

    [Fact]
    public void ElfSections_MapsVaViaLoadSegment() {
        var segments = ElfSections.ReadSegments(BuildElf());
        Assert.True(ElfSections.TryVaToFileOffset(segments, TextVAddr, out int fo));
        Assert.True(fo > 0);
        Assert.True(ElfSections.TryVaToFileOffset(segments, TextVAddr + 4, out int fo2));
        Assert.Equal(fo + 4, fo2);
        Assert.False(ElfSections.TryVaToFileOffset(segments, 0xDEAD0000ul, out _));
    }

    [Fact]
    public void ElfSections_FindsInitArray() {
        Assert.True(ElfSections.TryFindInitArray(BuildElf(), out ulong va, out ulong size));
        Assert.Equal(InitArrayVAddr, va);
        Assert.Equal(16ul, size);
    }

    [Fact]
    public void ElfSymbols_ReadsNamedSymbol() {
        var syms = ElfSymbols.Read(BuildElf());
        Assert.Contains(syms, s => s.Name == "myfunc" && s.Value == FuncValue);
    }

    [Fact]
    public void ElfSymbols_StrippedReturnsEmptyGracefully() {
        byte[] elf = BuildElf();
        elf[0x3C] = 1;
        elf[0x3D] = 0;
        var syms = ElfSymbols.Read(elf);
        Assert.Empty(syms);
    }

    [Fact]
    public void BinaryImage_LoadElf_ResolvesFuncAndText() {
        var img = BinaryImage.Load(BuildElf());
        Assert.IsType<ElfImage>(img);
        Assert.True(img!.TryFindText(out _, out int size, out ulong vmAddr));
        Assert.Equal(0x1000ul, vmAddr);
        Assert.Equal(8, size);
        Assert.True(img.TryFindFunc(["myfunc"], out var range));
        Assert.Equal(FuncValue, range.Start);
        Assert.True(img.TryGetInitArray(out ulong iva, out ulong isize));
        Assert.Equal(InitArrayVAddr, iva);
        Assert.Equal(16ul, isize);
    }

    [Fact]
    public void BinaryImage_LoadMachoMagic_ReturnsMachoImage() {
        byte[] macho = new byte[64];
        macho[0] = 0xCF;
        macho[1] = 0xFA;
        macho[2] = 0xED;
        macho[3] = 0xFE;
        Assert.IsType<MachoImage>(BinaryImage.Load(macho));
    }

    [Fact]
    public void BinaryImage_LoadUnknownMagic_ReturnsNull() {
        byte[] junk = [0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];
        Assert.Null(BinaryImage.Load(junk));
    }
}
