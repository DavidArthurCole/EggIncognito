using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class VehicleCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_vehicledata.cpp";
    private const long Stride = 0xF0;
    private const long CapacityOff = 0x18;

    public static Result Read(byte[] bin, string initSymbol = InitSymbol) {
        var img = BinaryImage.Load(bin);
        return ReadWith(bin, img?.Symbols ?? [], img?.Sections ?? [], initSymbol);
    }

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, string initSymbol = InitSymbol) {
        var scan = StructInitReader.ReadWith(bin, syms, initSymbol);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);
        if (scan.Structs.Count == 0) return new Result(false, [], "no struct bases");

        var s = MergeByAbsoluteVa(scan.Structs);
        var entries = new List<VehicleEntry>();
        for (int i = 0; ; i++) {
            long slot = i * Stride;
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
        StructInitReader.StructInit s, long nameOff) {
        if (s.TryPointer(nameOff, out ulong pva) && IsName(ReadCstr(bin, sections, pva)) is { } fromPtr)
            return fromPtr;
        return s.TryTemplate(nameOff, out ulong tva) && IsName(ReadCstr(bin, sections, tva)) is { } fromTpl
            ? fromTpl
            : s.TryInlineStringComplete(nameOff, out string inline) && IsName(inline) is { } fromInline
                ? fromInline
                : IsName(s.TryInlineString(nameOff));
    }

    private static bool IsPlausibleCapacity(long c) => c is > 0 and <= 1_000_000_000_000;

    private static string? IsName(string s) {
        if (s.Length < 2 || !(char.IsAsciiLetterUpper(s[0]) || char.IsAsciiDigit(s[0]))) return null;
        foreach (char c in s) {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not (' ' or '.' or '\''))
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

    public readonly record struct VehicleEntry(int Index, string? Name, long Capacity);

    public readonly record struct Result(bool Ok, IReadOnlyList<VehicleEntry> Entries, string Diagnostics);
}
