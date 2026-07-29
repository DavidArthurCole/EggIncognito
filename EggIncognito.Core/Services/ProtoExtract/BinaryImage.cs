using System.Runtime.CompilerServices;

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
    private readonly Lazy<IReadOnlyList<MachoSymbols.Symbol>> _symbols;
    private readonly Lazy<IReadOnlyList<MachoSections.Section>> _sections;

    public byte[] Bytes { get; }
    public IReadOnlyList<MachoSymbols.Symbol> Symbols => _symbols.Value;
    public IReadOnlyList<MachoSections.Section> Sections => _sections.Value;

    public MachoImage(byte[] bin) {
        Bytes = bin ?? [];
        _symbols = new Lazy<IReadOnlyList<MachoSymbols.Symbol>>(() => MachoSymbols.Read(Bytes));
        _sections = new Lazy<IReadOnlyList<MachoSections.Section>>(() => MachoSections.Read(Bytes));
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
    private readonly Lazy<IReadOnlyList<MachoSections.Section>> _sections;
    private readonly Lazy<IReadOnlyList<ElfSections.LoadSegment>> _segments;
    private readonly Lazy<IReadOnlyList<MachoSymbols.Symbol>> _symbols;

    public byte[] Bytes { get; }
    public IReadOnlyList<MachoSymbols.Symbol> Symbols => _symbols.Value;
    public IReadOnlyList<MachoSections.Section> Sections => _sections.Value;

    public ElfImage(byte[] bin) {
        Bytes = bin ?? [];
        _sections = new Lazy<IReadOnlyList<MachoSections.Section>>(() => ElfSections.Read(Bytes));
        _segments = new Lazy<IReadOnlyList<ElfSections.LoadSegment>>(() => ElfSections.ReadSegments(Bytes));
        _symbols = new Lazy<IReadOnlyList<MachoSymbols.Symbol>>(() => ElfSymbols.Read(Bytes));
    }

    public bool TryVaToFileOffset(ulong va, out int fileOff, out MachoSections.Section owner) {
        if (MachoSections.TryVaToFileOffset(Sections, va, out fileOff, out owner)) return true;
        owner = default;
        return ElfSections.TryVaToFileOffset(_segments.Value, va, out fileOff);
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

#pragma warning disable IDE0028
    private static readonly ConditionalWeakTable<byte[], IBinaryImage> Cache = new();
#pragma warning restore IDE0028

    public static IBinaryImage? Load(byte[] bin) {
        if (bin is null || bin.Length < 8) return null;
        if (Cache.TryGetValue(bin, out var cached)) return cached;

        IBinaryImage? img = Create(bin);
        if (img is not null) Cache.AddOrUpdate(bin, img);
        return img;
    }

    private static IBinaryImage? Create(byte[] bin) {
        if (bin[0] == 0x7F && bin[1] == (byte)'E' && bin[2] == (byte)'L' && bin[3] == (byte)'F')
            return new ElfImage(bin);

        uint magic = (uint)(bin[0] | (bin[1] << 8) | (bin[2] << 16) | (bin[3] << 24));
        if (magic is MachoMagic64 or MachoCigam64 or FatMagic or FatCigam)
            return new MachoImage(bin);

        return null;
    }
}
