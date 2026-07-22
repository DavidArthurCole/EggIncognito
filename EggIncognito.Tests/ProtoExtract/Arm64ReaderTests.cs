using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class Arm64ReaderTests {
    [Fact]
    public void ParseElem_accepts_known_types() {
        Assert.True(Arm64ConstSectionReader.TryParseElem("f64", out var a) && a == TableElemType.F64);
        Assert.True(Arm64ConstSectionReader.TryParseElem("F32", out var b) && b == TableElemType.F32);
        Assert.True(Arm64ConstSectionReader.TryParseElem("i32", out var c) && c == TableElemType.I32);
        Assert.False(Arm64ConstSectionReader.TryParseElem("bogus", out _));
    }

    [Fact]
    public void Sections_read_and_map_va() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var sections = MachoSections.Read(bin);
        Assert.NotEmpty(sections);
        Assert.NotNull(MachoSections.Find(sections, "__TEXT", "__text"));

        var text = MachoSections.Find(sections, "__TEXT", "__text")!.Value;
        Assert.True(MachoSections.TryVaToFileOffset(sections, text.VmAddr, out _, out var owner));
        Assert.Equal("__text", owner.Name);
    }

    [Fact]
    public void List_disassembles_a_known_function() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var lst = Arm64DataTableReader.List(bin, ["FarmScene10updateSilo"], 64);
        Assert.True(lst.Ok, lst.Diagnostics);
        Assert.NotEmpty(lst.Instructions);
        Assert.Contains(lst.Instructions, i => i.Mnemonic is "fmov" or "ldr" or "adrp");
    }

    [Fact]
    public void Dump_reads_typed_values_from_mapped_va() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var sections = MachoSections.Read(bin);
        var text = MachoSections.Find(sections, "__TEXT", "__text")!.Value;
        var dump = Arm64ConstSectionReader.Dump(bin, text.VmAddr, 4, TableElemType.U32);
        Assert.True(dump.Ok, dump.Diagnostics);
        Assert.Equal(4, dump.Values.Count);
    }

    [Fact]
    public void Dump_rejects_unmapped_va() {
        if (!BinaryFixture.TryLoad(out var bin)) return;
        var dump = Arm64ConstSectionReader.Dump(bin, 0x1, 4, TableElemType.F64);
        Assert.False(dump.Ok);
        Assert.Contains("not in any mapped section", dump.Diagnostics);
    }
}
