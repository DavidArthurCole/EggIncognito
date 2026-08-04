namespace EggIncognito.Services.ProtoExtract;

public static class VehicleCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_vehicledata.cpp";
    public const string SignatureString = "HYPERLOOP TRAIN";
    private const long Stride = 0xF0;
    private const long ElfStride = 0x140;
    private const long CapacityOff = 0x18;

    public static Result Read(byte[] bin, string initSymbol = InitSymbol) {
        var img = BinaryImage.Load(bin);
        return ReadWith(bin, img?.Symbols ?? [], img?.Sections ?? [], initSymbol);
    }

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, string initSymbol = InitSymbol) {
        if (BinaryImage.Load(bin) is ElfImage) return ReadElf(bin);
        var scan = StructInitReader.ReadWith(bin, syms, initSymbol);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Build(bin, sections, scan, Stride);
    }

    private static Result ReadElf(byte[] bin) {
        var loc = InitArrayLocator.Create(bin);
        if (loc is null) return new Result(false, [], "no binary image");
        var sections = BinaryImage.Load(bin)?.Sections ?? [];
        var best = new Result(false, [], "vehicledata init not located via signature on ELF");
        foreach ((ulong s0, ulong e0) in loc.LocateAllByString(SignatureString)) {
            var scan = StructInitReader.ReadRange(bin, s0, e0);
            if (!scan.Ok) continue;
            var built = Build(bin, sections, scan, ElfStride);
            if (built.Entries.Count > best.Entries.Count) best = built;
        }

        return best;
    }

    private static Result Build(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        StructInitReader.Result scan, long stride) {
        if (scan.Structs.Count == 0) return new Result(false, [], "no struct bases");

        var s = MergeByAbsoluteVa(scan.Structs);
        var entries = new List<VehicleEntry>();
        for (int i = 0; ; i++) {
            long slot = i * stride;
            if (!s.TryInt(slot + CapacityOff, 8, out long capacity) || !IsPlausibleCapacity(capacity)) break;
            entries.Add(new VehicleEntry(i, ResolveName(bin, sections, s, slot), capacity));
        }

        return new Result(true, entries, $"{entries.Count} vehicles, {entries.Count(e => e.Name is not null)} named");
    }

    private static StructInitReader.StructInit MergeByAbsoluteVa(IReadOnlyList<StructInitReader.StructInit> structs) {
        var origin = structs.MaxBy(x => x.Bytes.Count);
        var bytes = new Dictionary<long, byte>();
        var pointers = new Dictionary<long, ulong>();
        var templates = new Dictionary<long, ulong>();
        foreach (var s in structs.OrderBy(x => x.BaseVa)) {
            long rebase = unchecked((long)s.BaseVa - (long)origin.BaseVa);
            foreach ((long off, byte b) in s.Bytes) bytes[off + rebase] = b;
            foreach ((long off, ulong p) in s.Pointers) pointers[off + rebase] = p;
            foreach ((long off, ulong t) in s.Templates) templates[off + rebase] = t;
        }

        return new StructInitReader.StructInit(origin.BaseVa, bytes, pointers, templates);
    }

    private static string? ResolveName(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        StructInitReader.StructInit s, long nameOff)
        => ResolveAt(bin, sections, s, nameOff) ?? ResolveAt(bin, sections, s, nameOff + 1);

    private static string? ResolveAt(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        StructInitReader.StructInit s, long off) {
        if (s.TryPointer(off, out ulong pva) && IsName(ReadCstr(bin, sections, pva)) is { } fromPtr)
            return fromPtr;
        if (s.TryTemplate(off, out ulong tva) && IsName(ReadCstr(bin, sections, tva)) is { } fromTpl)
            return fromTpl;
        return s.TryInlineStringComplete(off, out string inline) && IsName(inline) is { } fromInline
            ? fromInline
            : IsName(s.TryInlineString(off));
    }

    private static bool IsPlausibleCapacity(long c) => c is > 0 and <= 1_000_000_000_000;

    private static readonly string[] NameSections = ["__cstring", "__const", ".rodata", ".data.rel.ro"];

    private static string? IsName(string s) => BinaryStrings.IsName(s, " .'", allowDigitStart: true);

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va)
        => BinaryStrings.ReadCstr(bin, sections, va, NameSections, 64);

    public readonly record struct VehicleEntry(int Index, string? Name, long Capacity);

    public readonly record struct Result(bool Ok, IReadOnlyList<VehicleEntry> Entries, string Diagnostics);
}
