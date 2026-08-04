using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class HabCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_habdata";
    public const string SignatureString = "PLANET PORTAL";
    private const long Stride = 0x158;
    private const long ElfStride = 0x1e0;
    private const long CapacityOffset = 0x18;

    private static readonly long[] ExpectedCapacities = [
        250, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000,
        1_000_000, 2_000_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000, 600_000_000
    ];

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? [], img?.Sections ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections) {
        if (BinaryImage.Load(bin) is ElfImage) return ExtractElf(bin);
        var scan = StructInitReader.ReadWith(bin, syms, InitSymbol);
        return !scan.Ok ? new Result(false, [], scan.Diagnostics) : Build(bin, sections, scan, Stride);
    }

    private static Result ExtractElf(byte[] bin) {
        var loc = InitArrayLocator.Create(bin);
        if (loc is null) return new Result(false, [], "no binary image");
        var sections = BinaryImage.Load(bin)?.Sections ?? [];
        foreach ((ulong s0, ulong e0) in loc.LocateAllByString(SignatureString)) {
            var scan = StructInitReader.ReadRange(bin, s0, e0);
            if (!scan.Ok) continue;
            var built = Build(bin, sections, scan, ElfStride);
            if (built.Ok) return built;
        }

        return new Result(false, [], "no habdata init with the 19-capacity anchor located via signature on ELF");
    }

    private static Result Build(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        StructInitReader.Result scan, long stride) {
        var bytes = new Dictionary<ulong, byte>();
        var ptrs = new Dictionary<ulong, ulong>();
        var tpls = new Dictionary<ulong, ulong>();
        foreach (var s in scan.Structs) {
            foreach ((long off, byte b) in s.Bytes) bytes[s.BaseVa + (ulong)off] = b;
            foreach ((long off, ulong p) in s.Pointers) ptrs[s.BaseVa + (ulong)off] = p;
            foreach ((long off, ulong t) in s.Templates) tpls[s.BaseVa + (ulong)off] = t;
        }

        foreach (var s in scan.Structs) {
            if (!TryReadInt64(bytes, s.BaseVa + CapacityOffset, out long first) || first != ExpectedCapacities[0])
                continue;
            if (TryReadBlock(bin, sections, bytes, ptrs, tpls, s.BaseVa, stride, out var entries))
                return new Result(true, entries, $"{entries.Count} habs, {entries.Count(e => e.Name is not null)} named");
        }

        return new Result(false, [], "no record base carries the ordered 19-capacity anchor sequence at +0x18");
    }

    private static bool TryReadBlock(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        Dictionary<ulong, byte> bytes, Dictionary<ulong, ulong> ptrs, Dictionary<ulong, ulong> tpls,
        ulong blockBase, long stride, out List<HabEntry> entries) {
        entries = [];
        for (int i = 0; i < ExpectedCapacities.Length; i++) {
            ulong rec = blockBase + (ulong)(i * stride);
            if (!TryReadInt64(bytes, rec + CapacityOffset, out long cap) || cap != ExpectedCapacities[i])
                return false;
            entries.Add(new HabEntry(i, ResolveName(bin, sections, bytes, ptrs, tpls, rec), cap));
        }

        return true;
    }

    private static string? ResolveName(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        Dictionary<ulong, byte> bytes, Dictionary<ulong, ulong> ptrs, Dictionary<ulong, ulong> tpls, ulong rec)
        => ResolveAt(bin, sections, bytes, ptrs, tpls, rec) ?? ResolveAt(bin, sections, bytes, ptrs, tpls, rec + 1);

    private static string? ResolveAt(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        Dictionary<ulong, byte> bytes, Dictionary<ulong, ulong> ptrs, Dictionary<ulong, ulong> tpls, ulong at) {
        if (ptrs.TryGetValue(at, out ulong pva) && IsName(ReadCstr(bin, sections, pva)) is { } fromPtr)
            return fromPtr;
        if (tpls.TryGetValue(at, out ulong tva) && IsName(ReadCstr(bin, sections, tva)) is { } fromTpl)
            return fromTpl;
        return IsName(ReadInline(bytes, at));
    }

    private static string ReadInline(Dictionary<ulong, byte> bytes, ulong start) {
        var sb = new StringBuilder();
        for (ulong va = start; va < start + 23; va++) {
            if (!bytes.TryGetValue(va, out byte b) || b == 0) break;
            if (b is < 0x20 or > 0x7e) break;
            sb.Append((char)b);
        }

        return sb.ToString();
    }

    private static readonly string[] NameSections = ["__cstring", "__const", ".rodata", ".data.rel.ro"];

    private static string? IsName(string s) => BinaryStrings.IsName(s, " ,.");

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va)
        => BinaryStrings.ReadCstr(bin, sections, va, NameSections, 64);

    private static bool TryReadInt64(Dictionary<ulong, byte> bytes, ulong start, out long value) {
        ulong raw = 0;
        for (int k = 0; k < 8; k++) {
            if (!bytes.TryGetValue(start + (ulong)k, out byte b)) {
                value = 0;
                return false;
            }

            raw |= (ulong)b << (k * 8);
        }

        value = (long)raw;
        return true;
    }

    public readonly record struct HabEntry(int Index, string? Name, long Capacity);

    public readonly record struct Result(bool Ok, IReadOnlyList<HabEntry> Entries, string Diagnostics);
}
