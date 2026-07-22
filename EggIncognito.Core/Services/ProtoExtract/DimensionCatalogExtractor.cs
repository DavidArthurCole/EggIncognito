namespace EggIncognito.Services.ProtoExtract;

public static class DimensionCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_boostmanager";

    public readonly record struct Result(bool Ok, IReadOnlyList<string> Ids, string Diagnostics);

    public static Result Extract(byte[] bin) => ExtractWith(bin, MachoSymbols.Read(bin), MachoSections.Read(bin));

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, IReadOnlyList<MachoSections.Section> sections) {
        var read = StaticInitCatalogReader.ReadWith(bin, syms, sections, InitSymbol, IsDimensionId);
        if (!read.Ok) return new(false, [], read.Diagnostics);

        var ids = read.Entries.Select(e => e.Id).ToList();
        return new(true, ids, $"{ids.Count} dimensions");
    }

    private static bool IsDimensionId(string s)
        => s.Length > 3 && s.StartsWith("bd-", StringComparison.Ordinal)
           && s.Skip(3).All(c => char.IsAsciiLetterLower(c) || c == '-');
}
