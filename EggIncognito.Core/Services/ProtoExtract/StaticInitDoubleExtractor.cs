using System.Globalization;

namespace EggIncognito.Services.ProtoExtract;

public static class StaticInitDoubleExtractor
{
    public readonly record struct Result(bool Ok, IReadOnlyList<double> Values, string Symbol, string Diagnostics);

    public static Result Extract(byte[] bin, string symbol, int maxInsns = 6000)
        => ExtractWith(bin, MachoSymbols.Read(bin), symbol, maxInsns);

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string symbol, int maxInsns = 6000)
    {
        var lst = Arm64DataTableReader.ListWith(bin, syms, [symbol], maxInsns);
        if (!lst.Ok) return new(false, [], symbol, lst.Diagnostics);
        return Decode(bin, lst.Instructions, symbol);
    }

    public static Result ExtractRange(byte[] bin, ulong startVa, ulong endVa, int maxInsns = 60000)
    {
        if (!MachoText.TryFindText(bin, out var textFileOff, out _, out var textVmAddr))
            return new(false, [], "range", "no __text");
        var slide = textVmAddr - (ulong)textFileOff;
        var startFile = (long)startVa - (long)slide;
        var len = (long)endVa - (long)startVa;
        if (startFile < 0 || len <= 0 || startFile + len > bin.Length)
            return new(false, [], "range", "range out of bounds");

        var code = new byte[len];
        Array.Copy(bin, startFile, code, 0, (int)len);
        using var cs = Gee.External.Capstone.CapstoneDisassembler.CreateArm64Disassembler(
            Gee.External.Capstone.Arm64.Arm64DisassembleMode.LittleEndian);
        var insns = new List<Arm64DataTableReader.Insn>();
        foreach (var ins in cs.Disassemble(code, (long)startVa))
        {
            insns.Add(new Arm64DataTableReader.Insn((ulong)ins.Address, ins.Mnemonic ?? "", ins.Operand ?? ""));
            if (insns.Count >= maxInsns) break;
        }
        return Decode(bin, insns, "range");
    }

    private static Result Decode(byte[] bin, IReadOnlyList<Arm64DataTableReader.Insn> insns, string symbol)
    {
        var sections = MachoSections.Read(bin);
        var seen = new HashSet<long>();
        var outp = new List<double>();
        var page = new Dictionary<string, ulong>();

        void Emit(long bits)
        {
            var d = BitConverter.Int64BitsToDouble(bits);
            if (!IsValue(d)) return;
            var key = BitConverter.DoubleToInt64Bits(d);
            if (seen.Add(key)) outp.Add(d);
        }

        for (var i = 0; i < insns.Count; i++)
        {
            var m = insns[i].Mnemonic;
            var ops = insns[i].Operands;

            if (m == "adrp" && TryReg(ops, out var ar) && TryLastImm(ops, out var apage))
            {
                page[ar] = (ulong)apage;
                continue;
            }
            if (m == "add" && TryReg(ops, out var dr) && TryAddBase(ops, page, out var full))
            {
                page[dr] = full;
                continue;
            }

            if ((m == "movz" || m == "orr") && TryComposeStart(m, ops, out var reg, out var acc))
            {
                var bits = acc;
                var j = i + 1;
                while (j < insns.Count && insns[j].Mnemonic == "movk"
                       && TryMovk(insns[j].Operands, reg, out var part, out var laneShift))
                {
                    var lane = 0xFFFFL << laneShift;
                    bits = (bits & ~lane) | part;
                    j++;
                }
                Emit(bits);
                continue;
            }

            if ((m == "ldr" || m == "ldur") && TryMemLoad(ops, page, out var va, out var isWide))
            {
                if (MachoSections.TryVaToFileOffset(sections, va, out var fo, out _))
                {
                    if (isWide && fo + 16 <= bin.Length)
                    {
                        Emit(BitConverter.ToInt64(bin, fo));
                        Emit(BitConverter.ToInt64(bin, fo + 8));
                    }
                    else if (!isWide && fo + 8 <= bin.Length)
                    {
                        Emit(BitConverter.ToInt64(bin, fo));
                    }
                }
            }
        }

        return new(true, outp, symbol, "ok");
    }

    private static bool IsValue(double d)
        => double.IsFinite(d) && Math.Abs(d) >= 1e-6 && Math.Abs(d) <= 1e12;

    private static bool TryReg(string ops, out string reg)
    {
        reg = "";
        var p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return false;
        reg = p[0];
        return reg.Length > 0;
    }

    private static bool TryLastImm(string ops, out long imm)
    {
        imm = 0;
        var p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var k = p.Length - 1; k >= 0; k--)
            if (p[k].StartsWith('#')) return ParseImm(p[k], out imm);
        return false;
    }

    private static bool TryAddBase(string ops, Dictionary<string, ulong> page, out ulong full)
    {
        full = 0;
        var p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return false;
        if (!page.TryGetValue(p[1], out var basePage)) return false;
        if (!p[2].StartsWith('#') || !ParseImm(p[2], out var off)) return false;
        full = basePage + (ulong)off;
        return true;
    }

    private static bool TryComposeStart(string mnemonic, string ops, out string reg, out long acc)
    {
        reg = ""; acc = 0;
        var p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return false;
        reg = p[0];

        if (mnemonic == "orr")
        {
            if (p.Length < 3) return false;
            if (p[1] is not ("xzr" or "wzr")) return false;
            if (!p[2].StartsWith('#') || !ParseImm(p[2], out var mask)) return false;
            acc = mask;
            return true;
        }

        if (!p[1].StartsWith('#') || !ParseImm(p[1], out var imm)) return false;
        var shift = 0;
        if (p.Length >= 3 && p[2].StartsWith("lsl", StringComparison.OrdinalIgnoreCase))
            shift = ParseShift(p[2]);
        acc = imm << shift;
        return true;
    }

    private static bool TryMovk(string ops, string reg, out long part, out int shift)
    {
        part = 0; shift = 0;
        var p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2 || p[0] != reg) return false;
        if (!p[1].StartsWith('#') || !ParseImm(p[1], out var imm)) return false;
        if (p.Length >= 3 && p[2].StartsWith("lsl", StringComparison.OrdinalIgnoreCase))
            shift = ParseShift(p[2]);
        part = (imm & 0xFFFF) << shift;
        return true;
    }

    private static bool TryMemLoad(string ops, Dictionary<string, ulong> page, out ulong va, out bool isWide)
    {
        va = 0; isWide = false;
        var p = ops.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 2) return false;
        isWide = p[0].StartsWith("q", StringComparison.OrdinalIgnoreCase);
        if (!isWide && !p[0].StartsWith("d", StringComparison.OrdinalIgnoreCase)) return false;

        var mem = p[1];
        var open = mem.IndexOf('[');
        if (open < 0) return false;
        var body = mem[(open + 1)..].TrimEnd(']', '!', ' ');
        var mp = body.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (mp.Length == 0 || !page.TryGetValue(mp[0], out var basePage)) return false;
        long disp = 0;
        if (mp.Length >= 2 && mp[1].StartsWith('#')) ParseImm(mp[1], out disp);
        va = basePage + (ulong)disp;
        return true;
    }

    private static int ParseShift(string tok)
    {
        var sh = tok.Split('#', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return sh.Length == 2 && int.TryParse(sh[1], out var s) ? s : 0;
    }

    private static bool ParseImm(string tok, out long imm)
    {
        imm = 0;
        var t = tok.StartsWith('#') ? tok[1..] : tok;
        var neg = false;
        if (t.StartsWith('-')) { neg = true; t = t[1..]; }
        var ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u)
            : ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out u);
        if (!ok) return false;
        imm = neg ? -(long)u : unchecked((long)u);
        return true;
    }
}
