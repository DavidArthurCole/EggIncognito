using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.ProtoExtract;

public static partial class MissionCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_missiondata";

    [GeneratedRegex("^[a-z][a-z0-9_]{3,}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9 .!']+$")]
    private static partial Regex DisplayPattern();

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? [], img?.Sections ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections) {
        var scan = Arm64DataTableReader.ScanWith(bin, syms, [InitSymbol]);
        if (!scan.Ok) return new Result(false, [], scan.Diagnostics);

        var entries = new List<MissionEntry>();
        string? pendingDisplay = null;
        int open = -1;
        string? prevStr = null;
        ulong prevVa = 0;

        foreach (var r in scan.Addresses) {
            if (r.Section != "__cstring") continue;
            string raw = ReadCstr(bin, sections, r.Va);
            if (string.IsNullOrEmpty(raw)) continue;
            string str = StripDescPrefix(raw);

            if (prevStr is not null && prevStr.EndsWith(str, StringComparison.Ordinal)
                                    && r.Va > prevVa && r.Va <= prevVa + (ulong)prevStr.Length + 1) {
                continue;
            }

            prevStr = str;
            prevVa = r.Va;

            if (IdPattern().IsMatch(str)) {
                entries.Add(new MissionEntry(str, pendingDisplay, null));
                open = entries.Count - 1;
                pendingDisplay = null;
            } else if (DisplayPattern().IsMatch(str)) {
                pendingDisplay = str;
                open = -1;
            } else if (open >= 0 && entries[open].Goal is null) {
                entries[open] = entries[open] with { Goal = str };
            }
        }

        return new Result(true, entries, $"{entries.Count} missions");
    }

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va) {
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out var owner)) return "";
        if (owner.Name != "__cstring") return "";
        int end = fo;
        while (end < bin.Length && bin[end] != 0 && end - fo < 128) end++;
        return Encoding.UTF8.GetString(bin, fo, end - fo);
    }

    private static string StripDescPrefix(string s) {
        string t = s;
        if (t.Length > 0 && t[0] == (char)0x1b) t = t[1..];
        return t;
    }

    public readonly record struct MissionEntry(string Id, string? DisplayName, string? Goal);

    public readonly record struct Result(bool Ok, IReadOnlyList<MissionEntry> Entries, string Diagnostics);
}
