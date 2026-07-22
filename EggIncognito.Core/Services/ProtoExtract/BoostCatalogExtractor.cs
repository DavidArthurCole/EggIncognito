using System.Text.RegularExpressions;

namespace EggIncognito.Services.ProtoExtract;

public static partial class BoostCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_boostmanager";

    public readonly record struct BoostEntry(string Id, string? DisplayName, string? Description);
    public readonly record struct Result(bool Ok, IReadOnlyList<BoostEntry> Entries, string Diagnostics);

    [GeneratedRegex("^[a-z][a-z0-9_]+$")]
    private static partial Regex IdPattern();

    public static Result Extract(byte[] bin) => ExtractWith(bin, MachoSymbols.Read(bin), MachoSections.Read(bin));

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, IReadOnlyList<MachoSections.Section> sections) {
        var read = StaticInitCatalogReader.ReadWith(bin, syms, sections, InitSymbol, IsBoostId);
        if (!read.Ok) return new(false, [], read.Diagnostics);

        var outp = read.Entries.Select(e => new BoostEntry(e.Id, e.DisplayName, e.Description)).ToList();
        return new(true, outp, $"{outp.Count} boosts");
    }

    private static bool IsBoostId(string s)
        => s.Length >= 4 && IdPattern().IsMatch(s) && !s.StartsWith("bd", StringComparison.Ordinal);
}
