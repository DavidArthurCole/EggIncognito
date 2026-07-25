using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class StaticInitCatalogReader {
    public static Result Read(byte[] bin, string initSymbol, Func<string, bool> isId)
        => ReadWith(bin, MachoSymbols.Read(bin), MachoSections.Read(bin), initSymbol, isId);

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, string initSymbol, Func<string, bool> isId) {
        var scan = Arm64DataTableReader.ScanWith(bin, syms, [initSymbol]);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);

        var refs = new List<(ulong Va, string Str)>();
        foreach (var a in scan.Addresses) {
            if (a.Section != "__cstring") continue;
            refs.Add((a.Va, ReadCstr(bin, sections, a.Va)));
        }

        var kept = new List<(ulong Va, string Str)>();
        foreach (var c in refs) {
            if (c.Str.Length == 0) continue;
            if (kept.Count > 0) {
                (ulong Va, string Str) = kept[^1];
                ulong pEnd = Va + (ulong)Encoding.UTF8.GetByteCount(Str) + 1;
                if (c.Va > Va && c.Va < pEnd && Str.EndsWith(c.Str, StringComparison.Ordinal))
                    continue;
            }

            kept.Add(c);
        }

        var entries = new List<(string Id, string? Name, string? Desc)>();
        foreach ((ulong Va, string Str) in kept) {
            if (isId(Str)) {
                entries.Add((Str, null, null));
                continue;
            }

            if (entries.Count == 0) continue;

            var cur = entries[^1];
            if (TryDescription(Str, out string desc))
                entries[^1] = cur with { Desc = cur.Desc ?? desc };
            else if (cur.Name is null && LooksLikeDisplayName(Str))
                entries[^1] = cur with { Name = Str };
        }

        var outp = entries.Select(e => new Entry(e.Id, e.Name, e.Desc)).ToList();
        return new Result(true, outp, $"{outp.Count} entries");
    }

    private static bool LooksLikeDisplayName(string s)
        => s.Length > 0 && (s.Contains(' ') || char.IsUpper(s[0]) || char.IsDigit(s[0]));

    private static bool TryDescription(string s, out string desc) {
        desc = "";
        if (s.Length < 2 || s[0] != '\x1b') return false;
        string body = s[1] == 'z' ? s[2..] : s[1..];
        if (body.Length == 0) return false;
        desc = body;
        return true;
    }

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va) {
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out _)) return "";
        int end = fo;
        while (end < bin.Length && bin[end] != 0) end++;
        return Encoding.UTF8.GetString(bin, fo, end - fo);
    }

    public readonly record struct Entry(string Id, string? DisplayName, string? Description);

    public readonly record struct Result(bool Ok, IReadOnlyList<Entry> Entries, string Diagnostics);
}
