namespace EggIncognito.Services.ProtoExtract;

public interface IBinaryImage {
    byte[] Bytes { get; }
    IReadOnlyList<MachoSymbols.Symbol> Symbols { get; }
    IReadOnlyList<MachoSections.Section> Sections { get; }
    bool TryVaToFileOffset(ulong va, out int fileOff, out MachoSections.Section owner);
    bool TryFindFunc(string[] needles, out MachoSymbols.FuncRange range);
    bool TryFindText(out int fileOff, out int size, out ulong vmAddr);
    bool TryGetInitArray(out ulong va, out ulong size);
}

public sealed class MachoImage : IBinaryImage {
    public byte[] Bytes { get; }
    public IReadOnlyList<MachoSymbols.Symbol> Symbols { get; }
    public IReadOnlyList<MachoSections.Section> Sections { get; }

    public MachoImage(byte[] bin) {
        Bytes = bin ?? [];
        Symbols = MachoSymbols.Read(Bytes);
        Sections = MachoSections.Read(Bytes);
    }

    public bool TryVaToFileOffset(ulong va, out int fileOff, out MachoSections.Section owner)
        => MachoSections.TryVaToFileOffset(Sections, va, out fileOff, out owner);

    public bool TryFindFunc(string[] needles, out MachoSymbols.FuncRange range)
        => MachoSymbols.TryFindFunc(Symbols, needles, out range);

    public bool TryFindText(out int fileOff, out int size, out ulong vmAddr)
        => MachoText.TryFindText(Bytes, out fileOff, out size, out vmAddr);

    public bool TryGetInitArray(out ulong va, out ulong size) {
        var s = MachoSections.Find(Sections, "__DATA_CONST", "__mod_init_func")
                ?? MachoSections.Find(Sections, "__DATA", "__mod_init_func");
        if (s is null) {
            va = 0;
            size = 0;
            return false;
        }

        va = s.Value.VmAddr;
        size = s.Value.VmSize;
        return va != 0 && size != 0;
    }
}

public sealed class ElfImage : IBinaryImage {
    private readonly IReadOnlyList<ElfSections.LoadSegment> _segments;

    public byte[] Bytes { get; }
    public IReadOnlyList<MachoSymbols.Symbol> Symbols { get; }
    public IReadOnlyList<MachoSections.Section> Sections { get; }

    public ElfImage(byte[] bin) {
        Bytes = bin ?? [];
        Sections = ElfSections.Read(Bytes);
        _segments = ElfSections.ReadSegments(Bytes);
        Symbols = ElfSymbols.Read(Bytes);
    }

    public bool TryVaToFileOffset(ulong va, out int fileOff, out MachoSections.Section owner) {
        if (MachoSections.TryVaToFileOffset(Sections, va, out fileOff, out owner)) return true;
        owner = default;
        return ElfSections.TryVaToFileOffset(_segments, va, out fileOff);
    }

    public bool TryFindFunc(string[] needles, out MachoSymbols.FuncRange range)
        => MachoSymbols.TryFindFunc(Symbols, needles, out range);

    public bool TryFindText(out int fileOff, out int size, out ulong vmAddr)
        => ElfText.TryFindText(Bytes, out fileOff, out size, out vmAddr);

    public bool TryGetInitArray(out ulong va, out ulong size)
        => ElfSections.TryFindInitArray(Bytes, out va, out size);
}

public static class BinaryImage {
    private const uint MachoMagic64 = 0xFEEDFACF;
    private const uint MachoCigam64 = 0xCFFAEDFE;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatCigam = 0xBEBAFECA;

    public static IBinaryImage? Load(byte[] bin) {
        if (bin is null || bin.Length < 8) return null;
        if (bin[0] == 0x7F && bin[1] == (byte)'E' && bin[2] == (byte)'L' && bin[3] == (byte)'F')
            return new ElfImage(bin);

        uint magic = (uint)(bin[0] | (bin[1] << 8) | (bin[2] << 16) | (bin[3] << 24));
        if (magic is MachoMagic64 or MachoCigam64 or FatMagic or FatCigam)
            return new MachoImage(bin);

        return null;
    }
}
