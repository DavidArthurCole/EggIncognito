namespace EggIncognito.Services.ProtoExtract;

public static class DimensionCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_boostmanager";

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms) {
        if (BinaryImage.Load(bin) is ElfImage) return ExtractElf(bin);

        var read = StaticInitCatalogReader.ReadWith(bin, syms, InitSymbol, IsDimensionId);
        if (!read.Ok) return new Result(false, [], read.Diagnostics);

        var ids = read.Entries.Select(e => e.Id).ToList();
        return new Result(true, ids, $"{ids.Count} dimensions");
    }

    private static Result ExtractElf(byte[] bin) {
        var loc = InitArrayLocator.Create(bin);
        if (loc is null || !loc.TryLocateByString(BoostCatalogExtractor.SignatureString, out ulong s, out ulong e))
            return new Result(false, [], "boostmanager init not located via signature on ELF");

        var read = StaticInitCatalogReader.ReadRange(bin, s, e, IsDimensionId);
        if (!read.Ok) return new Result(false, [], read.Diagnostics);

        var ids = read.Entries.Select(x => x.Id).ToList();
        return new Result(true, ids, $"{ids.Count} dimensions (ELF)");
    }

    private static bool IsDimensionId(string s)
        => s.Length > 3 && s.StartsWith("bd-", StringComparison.Ordinal)
                        && s.Skip(3).All(c => char.IsAsciiLetterLower(c) || c == '-');

    public readonly record struct Result(bool Ok, IReadOnlyList<string> Ids, string Diagnostics);
}
