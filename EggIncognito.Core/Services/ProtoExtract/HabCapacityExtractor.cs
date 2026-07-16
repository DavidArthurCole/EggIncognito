using System.Globalization;

namespace EggIncognito.Services.ProtoExtract;

public static class HabCapacityExtractor
{
    public const string InitSymbol = "__GLOBAL__sub_I_habdata";

    public readonly record struct Result(bool Ok, IReadOnlyList<long> Capacities, string Symbol, string Diagnostics);

    private static readonly long[] Expected =
        [250, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000,
         1_000_000, 2_000_000, 5_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000, 600_000_000];

    public static Result Extract(byte[] bin) => ExtractWith(bin, MachoSymbols.Read(bin));

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms)
    {
        var lst = Arm64DataTableReader.ListWith(bin, syms, [InitSymbol], 6000);
        if (!lst.Ok) return new(false, [], InitSymbol, lst.Diagnostics);

        var caps = new List<long>();
        var insns = lst.Instructions;
        for (var i = 0; i < insns.Count; i++)
        {
            if (insns[i].Mnemonic != "movz") continue;
            if (!TryImm(insns[i].Operands, out var reg, out var lo, out var loShift) || loShift != 0) continue;

            if (IsCapValue(lo)) caps.Add(lo);

            if (i + 1 < insns.Count && insns[i + 1].Mnemonic == "movk"
                && TryImm(insns[i + 1].Operands, out var reg2, out var hi, out var hiShift)
                && reg2 == reg && hiShift == 16)
            {
                var merged = lo | (hi << 16);
                if (IsCapValue(merged)) caps.Add(merged);
            }
        }

        var trimmed = MatchExpectedPrefix(caps);
        var ok = trimmed.Count == Expected.Length;
        return new(ok, trimmed, InitSymbol,
            ok ? "ok" : $"expected {Expected.Length} caps, extracted {trimmed.Count} from {caps.Count} candidates");
    }

    private static bool IsCapValue(long v) => v is >= 250 and <= 600_000_000;

    private static List<long> MatchExpectedPrefix(IReadOnlyList<long> candidates)
    {
        var run = new List<long>();
        var e = 0;
        foreach (var c in candidates)
        {
            if (e >= Expected.Length) break;
            if (c == Expected[e]) { run.Add(c); e++; }
        }
        return run;
    }

    private static bool TryImm(string operands, out string reg, out long imm, out int shift)
    {
        reg = ""; imm = 0; shift = 0;
        var parts = operands.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        reg = parts[0];
        var immTok = parts[1];
        if (!immTok.StartsWith('#')) return false;
        immTok = immTok[1..];
        var val = immTok.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(immTok[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out imm)
            : long.TryParse(immTok, NumberStyles.Integer, CultureInfo.InvariantCulture, out imm);
        if (!val) return false;
        if (parts.Length >= 3 && parts[2].StartsWith("lsl", StringComparison.OrdinalIgnoreCase))
        {
            var sh = parts[2].Split('#', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (sh.Length == 2) int.TryParse(sh[1], out shift);
        }
        return true;
    }
}
