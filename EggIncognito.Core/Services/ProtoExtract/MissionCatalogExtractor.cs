using System.Text.RegularExpressions;

namespace EggIncognito.Services.ProtoExtract;

public static partial class MissionCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_missiondata";

    public readonly record struct MissionEntry(string Id, string? DisplayName, string? Goal);
    public readonly record struct Result(bool Ok, IReadOnlyList<MissionEntry> Entries, string Diagnostics);

    [GeneratedRegex("^[a-z][a-z0-9_]{3,}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9 .!']+$")]
    private static partial Regex DisplayPattern();

    public static Result Extract(byte[] bin) => ExtractWith(bin, MachoSymbols.Read(bin), MachoSections.Read(bin));

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, IReadOnlyList<MachoSections.Section> sections) {
        var scan = Arm64DataTableReader.ScanWith(bin, syms, [InitSymbol]);
        if (!scan.Ok) return new(false, [], scan.Diagnostics);

        var entries = new List<MissionEntry>();
        string? pendingDisplay = null;
        var open = -1;
        string? prevStr = null;
        ulong prevVa = 0;

        foreach (var r in scan.Addresses) {
            if (r.Section != "__cstring") continue;
            var raw = ReadCstr(bin, sections, r.Va);
            if (string.IsNullOrEmpty(raw)) continue;
            var str = StripDescPrefix(raw);

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
        return new(true, entries, $"{entries.Count} missions");
    }

    private static string ReadCstr(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va) {
        if (!MachoSections.TryVaToFileOffset(sections, va, out var fo, out var owner)) return "";
        if (owner.Name != "__cstring") return "";
        var end = fo;
        while (end < bin.Length && bin[end] != 0 && end - fo < 128) end++;
        return System.Text.Encoding.UTF8.GetString(bin, fo, end - fo);
    }

    private static string StripDescPrefix(string s) {
        var t = s;
        if (t.Length > 0 && t[0] == (char)0x1b) t = t[1..];
        return t;
    }
}
