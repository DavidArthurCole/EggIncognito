using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class HabCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_habdata";
    private const long Stride = 0x158;
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
        var scan = StructInitReader.ReadWith(bin, syms, InitSymbol);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);

        var bytes = new Dictionary<ulong, byte>();
        var ptrs = new Dictionary<ulong, ulong>();
        foreach (var s in scan.Structs) {
            foreach ((long off, byte b) in s.Bytes) bytes[s.BaseVa + (ulong)off] = b;
            foreach ((long off, ulong p) in s.Pointers) ptrs[s.BaseVa + (ulong)off] = p;
        }

        foreach (var s in scan.Structs) {
            if (!TryReadInt64(bytes, s.BaseVa + CapacityOffset, out long first) || first != ExpectedCapacities[0])
                continue;
            if (TryReadBlock(bin, sections, bytes, ptrs, s.BaseVa, out var entries))
                return new Result(true, entries, $"{entries.Count} habs, {entries.Count(e => e.Name is not null)} named");
        }

        return new Result(false, [], "no record base carries the ordered 19-capacity anchor sequence at +0x18");
    }

    private static bool TryReadBlock(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        Dictionary<ulong, byte> bytes, Dictionary<ulong, ulong> ptrs, ulong blockBase,
        out List<HabEntry> entries) {
        entries = [];
        for (int i = 0; i < ExpectedCapacities.Length; i++) {
            ulong rec = blockBase + (ulong)(i * Stride);
            if (!TryReadInt64(bytes, rec + CapacityOffset, out long cap) || cap != ExpectedCapacities[i])
                return false;
            entries.Add(new HabEntry(i, ResolveName(bin, sections, bytes, ptrs, rec), cap));
        }

        return true;
    }

    private static string? ResolveName(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        Dictionary<ulong, byte> bytes, Dictionary<ulong, ulong> ptrs, ulong rec) {
        if (ptrs.TryGetValue(rec, out ulong pva) && IsName(ReadCstr(bin, sections, pva)) is { } fromPtr)
            return fromPtr;
        return IsName(ReadInline(bytes, rec));
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

    private static string? IsName(string s) {
        if (s.Length < 2 || !char.IsAsciiLetterUpper(s[0])) return null;
        foreach (char c in s) {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not (' ' or ',' or '.'))
                return null;
        }

        return s;
    }

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va) {
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out var owner)) return "";
        if (owner.Name is not "__cstring" and not "__const") return "";
        int end = fo;
        while (end < bin.Length && bin[end] != 0 && end - fo < 64) end++;
        return Encoding.UTF8.GetString(bin, fo, end - fo);
    }

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
