using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class EggCatalogExtractor {
    public readonly record struct EggEntry(int Index, string? Name, double BaseValue);
    public readonly record struct Result(bool Ok, IReadOnlyList<EggEntry> Entries, string Diagnostics);

    public static Result Read(byte[] bin, string initSymbol = "__GLOBAL__sub_I_eggdata.cpp")
        => ReadWith(bin, MachoSymbols.Read(bin), MachoSections.Read(bin), initSymbol);

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, string initSymbol = "__GLOBAL__sub_I_eggdata.cpp") {
        var scan = StructInitReader.ReadWith(bin, syms, initSymbol);
        if (!scan.Ok) return new(false, [], scan.Diagnostics);

        var entries = new List<EggEntry>();
        for (var i = 0; i < scan.Structs.Count; i++) {
            var s = scan.Structs[i];
            var valueOff = i == 0 ? 0x30L : -0x10L;
            if (!s.TryFloat64(valueOff, out var value) || !IsPlausible(value)) break;

            var nameOff = i == 0 ? 0x00L : -0x40L;
            entries.Add(new EggEntry(i, ResolveName(bin, sections, s, nameOff), value));
        }
        return new(true, entries, $"{entries.Count} eggs, {entries.Count(e => e.Name is not null)} named");
    }

    private static string? ResolveName(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        StructInitReader.StructInit s, long nameOff) {
        if (s.TryPointer(nameOff, out var pva) && IsName(ReadCstr(bin, sections, pva)) is { } fromPtr)
            return fromPtr;
        return s.TryTemplate(nameOff, out var tva) && IsName(ReadCstr(bin, sections, tva)) is { } fromTpl
            ? fromTpl
            : IsName(s.TryInlineString(nameOff));
    }

    private static string? IsName(string s) {
        if (s.Length < 2 || !char.IsAsciiLetterUpper(s[0])) return null;
        foreach (var c in s)
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c is not (' ' or '.')) return null;
        return s;
    }

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va) {
        if (!MachoSections.TryVaToFileOffset(sections, va, out var fo, out var owner)) return "";
        if (owner.Name is not "__cstring" and not "__const") return "";
        var end = fo;
        while (end < bin.Length && bin[end] != 0 && end - fo < 64) end++;
        return Encoding.UTF8.GetString(bin, fo, end - fo);
    }

    private static bool IsPlausible(double d) => double.IsFinite(d) && Math.Abs(d) is >= 1e-9 and <= 1e18;
}
