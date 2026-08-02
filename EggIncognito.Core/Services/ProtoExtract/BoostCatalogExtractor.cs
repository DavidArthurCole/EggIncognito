using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Services.ProtoExtract;

public static partial class BoostCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_boostmanager";
    public const string SignatureString = "TACHYON PRISM";

    private const int StructStride = 0x88;
    private const int DescFieldOffset = 0x30;

    [GeneratedRegex("^[a-z][a-z0-9_]+$")]
    private static partial Regex IdPattern();

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? [], img?.Sections ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections) {
        if (BinaryImage.Load(bin) is ElfImage) return ExtractElf(bin);

        var read = StaticInitCatalogReader.ReadWith(bin, syms, InitSymbol, IsBoostId);
        if (!read.Ok) return new Result(false, [], read.Diagnostics);

        var decoded = DecodeMemberDescriptions(bin, syms, sections, read.Entries);
        if (decoded is null) {
            var fallback = read.Entries.Select(e => new BoostEntry(e.Id, e.DisplayName, e.Description)).ToList();
            int fallbackDesc = fallback.Count(e => e.Description is not null);
            return new Result(true, fallback,
                $"{fallback.Count} boosts, {fallbackDesc} with description, member decode unavailable, reader descriptions only");
        }

        var outp = read.Entries
            .Select((e, k) => new BoostEntry(e.Id, e.DisplayName, decoded.GetValueOrDefault(k)))
            .ToList();
        return new Result(true, outp, $"{outp.Count} boosts, {decoded.Count} with description");
    }

    private static Result ExtractElf(byte[] bin) {
        var loc = InitArrayLocator.Create(bin);
        if (loc is null || !loc.TryLocateByString(SignatureString, out ulong s, out ulong e))
            return new Result(false, [], $"boostmanager init not located via '{SignatureString}' on ELF");

        var read = StaticInitCatalogReader.ReadRange(bin, s, e, IsBoostId);
        if (!read.Ok) return new Result(false, [], read.Diagnostics);

        var outp = read.Entries.Select(x => new BoostEntry(x.Id, x.DisplayName, x.Description)).ToList();
        int desc = outp.Count(x => x.Description is not null);
        return new Result(true, outp,
            $"{outp.Count} boosts, {desc} with description (ELF catalog; member-store decode is Mach-O only)");
    }

    private static bool IsBoostId(string s)
        => s.Length >= 4 && IdPattern().IsMatch(s) && !s.StartsWith("bd", StringComparison.Ordinal);

    private static Dictionary<int, string>? DecodeMemberDescriptions(byte[] bin,
        IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections, IReadOnlyList<StaticInitCatalogReader.Entry> entries) {
        var expected = new Dictionary<int, string>();
        for (int k = 0; k < entries.Count; k++) {
            if (entries[k].Description is { } d)
                expected[k] = d;
        }

        if (expected.Count == 0) return null;

        var lst = Arm64DataTableReader.ListWith(bin, syms, [InitSymbol], 100_000);
        if (!lst.Ok) return null;

        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var allocRegs = new HashSet<string>(StringComparer.Ordinal);
        ulong? x0Imm = null;
        ulong? activeAlloc = null;
        var storeTargets = new List<ulong>();
        var descStores = new List<(ulong Va, ulong AllocSize)>();
        var sizeText = new Dictionary<ulong, string>();
        bool conflict = false;

        void ClearX0Imm(string reg) {
            if (reg is "x0" or "w0") x0Imm = null;
        }

        void SetPage(string reg, ulong va) {
            page[reg] = va;
            allocRegs.Remove(reg);
            ClearX0Imm(reg);
        }

        void ClobberReg(string reg) {
            page.Remove(reg);
            allocRegs.Remove(reg);
            ClearX0Imm(reg);
        }

        void SetImm(string reg, ulong value) {
            page.Remove(reg);
            allocRegs.Remove(reg);
            if (reg is "x0" or "w0") x0Imm = value;
        }

        bool RecordStoreTarget(string memToken, out ulong va) {
            va = 0;
            if (!TryMem(memToken, out string baseReg, out ulong off) ||
                !page.TryGetValue(baseReg, out ulong baseVa)) {
                return false;
            }

            va = baseVa + off;
            storeTargets.Add(va);
            return true;
        }

        foreach (var i in lst.Instructions) {
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "adrp":
                    if (ops.Count == 2 && TryImm(ops[1], out ulong pg)) SetPage(ops[0], pg);
                    else if (ops.Count >= 1) ClobberReg(ops[0]);
                    break;

                case "add":
                    if (ops.Count == 3 && page.TryGetValue(ops[1], out ulong addBase) &&
                        TryImm(ops[2], out ulong addOff)) {
                        ulong full = addBase + addOff;
                        SetPage(ops[0], full);
                        if (activeAlloc is { } sz && TryReadDescription(bin, sections, full, out string text)) {
                            if (sizeText.TryGetValue(sz, out string? prev) && prev != text) conflict = true;
                            else sizeText[sz] = text;
                        }
                    } else if (ops.Count >= 1 && LooksLikeGpr(ops[0])) {
                        ClobberReg(ops[0]);
                    }

                    break;

                case "mov":
                    if (ops.Count == 2 && LooksLikeGpr(ops[0])) {
                        if (page.TryGetValue(ops[1], out ulong mv)) {
                            SetPage(ops[0], mv);
                        } else if (allocRegs.Contains(ops[1])) {
                            page.Remove(ops[0]);
                            ClearX0Imm(ops[0]);
                            allocRegs.Add(ops[0]);
                        } else if (TryImm(ops[1], out ulong mc)) {
                            SetImm(ops[0], mc);
                        } else {
                            ClobberReg(ops[0]);
                        }
                    }

                    break;

                case "movz":
                    if (ops.Count >= 2 && LooksLikeGpr(ops[0]) && TryImm(ops[1], out ulong mz))
                        SetImm(ops[0], mz << ShiftOf(ops));
                    else if (ops.Count >= 1) ClobberReg(ops[0]);
                    break;

                case "orr":
                    if (ops.Count == 3 && LooksLikeGpr(ops[0]) && ops[1] is "wzr" or "xzr" &&
                        TryImm(ops[2], out ulong oi)) {
                        SetImm(ops[0], oi);
                    } else if (ops.Count >= 1) {
                        ClobberReg(ops[0]);
                    }

                    break;

                case "bl":
                case "blr":
                    if (x0Imm is { } allocSize) {
                        activeAlloc = allocSize;
                        allocRegs.Clear();
                        allocRegs.Add("x0");
                    } else {
                        for (int r = 0; r <= 17; r++)
                            allocRegs.Remove("x" + r.ToString(CultureInfo.InvariantCulture));
                    }

                    x0Imm = null;
                    for (int r = 0; r <= 17; r++) {
                        page.Remove("x" + r.ToString(CultureInfo.InvariantCulture));
                        page.Remove("w" + r.ToString(CultureInfo.InvariantCulture));
                    }

                    break;

                case "str":
                case "stur":
                    if (ops.Count >= 2 && RecordStoreTarget(ops[^1], out ulong strVa)
                                       && activeAlloc is { } strAlloc && allocRegs.Contains(ops[0])) {
                        descStores.Add((strVa, strAlloc));
                    }

                    break;

                case "strb":
                case "sturb":
                case "strh":
                case "sturh":
                    if (ops.Count >= 2) RecordStoreTarget(ops[^1], out _);
                    break;

                case "stp":
                    if (ops.Count >= 3) RecordStoreTarget(ops[^1], out _);
                    break;

                case "ldr":
                case "ldur":
                    if (ops.Count >= 1 && LooksLikeGpr(ops[0])) ClobberReg(ops[0]);
                    break;

                case "ldp":
                    if (ops.Count >= 2) {
                        if (LooksLikeGpr(ops[0])) ClobberReg(ops[0]);
                        if (LooksLikeGpr(ops[1])) ClobberReg(ops[1]);
                    }

                    break;

                default:
                    if (!IsNonWriting(i.Mnemonic) && ops.Count >= 1 && LooksLikeGpr(ops[0])) ClobberReg(ops[0]);
                    break;
            }
        }

        if (conflict || sizeText.Count == 0 || descStores.Count == 0) return null;

        var candidates = new HashSet<ulong>(storeTargets);
        foreach ((ulong va, ulong _) in descStores) {
            for (int k = 0; k < entries.Count; k++) {
                ulong rel = (ulong)(DescFieldOffset + k * StructStride);
                if (va >= rel) candidates.Add(va - rel);
            }
        }

        foreach (ulong candidateBase in candidates.Order()) {
            if (TryGroupsForBase(candidateBase, descStores, sizeText, entries.Count, expected, out var groups))
                return groups;
        }

        return null;
    }

    private static bool TryGroupsForBase(ulong baseVa, IReadOnlyList<(ulong Va, ulong AllocSize)> descStores,
        Dictionary<ulong, string> sizeText, int entryCount, IReadOnlyDictionary<int, string> expected,
        out Dictionary<int, string> groups) {
        groups = [];
        foreach ((ulong va, ulong size) in descStores) {
            if (va < baseVa) continue;
            ulong rel = va - baseVa;
            if (rel % StructStride != DescFieldOffset) continue;
            int k = (int)(rel / StructStride);
            if (k >= entryCount) continue;
            if (!sizeText.TryGetValue(size, out string? text)) continue;
            if (groups.TryGetValue(k, out string? prev) && prev != text) return false;
            groups[k] = text;
        }

        if (groups.Count == 0) return false;
        foreach (var kv in expected) {
            if (!groups.TryGetValue(kv.Key, out string? g) || g != kv.Value)
                return false;
        }

        return true;
    }

    private static bool TryReadDescription(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va,
        out string desc) {
        desc = "";
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out var owner)) return false;
        if (owner.Name != "__cstring") return false;
        int end = fo;
        while (end < bin.Length && bin[end] != 0) end++;
        string s = Encoding.UTF8.GetString(bin, fo, end - fo);
        if (s.Length < 2 || s[0] != '\x1b') return false;
        string body = s[1] == 'z' ? s[2..] : s[1..];
        if (body.Length == 0) return false;
        desc = body;
        return true;
    }

    private static bool IsNonWriting(string mnemonic)
        => mnemonic is "cmp" or "cmn" or "ccmp" or "tst" or "fcmp" or "b" or "ret" or "nop"
           || mnemonic.StartsWith("b.", StringComparison.Ordinal)
           || mnemonic.StartsWith("cb", StringComparison.Ordinal)
           || mnemonic.StartsWith("tb", StringComparison.Ordinal);

    private static bool LooksLikeGpr(string tok)
        => tok.Length >= 2 && (tok[0] == 'x' || tok[0] == 'w') && char.IsDigit(tok[1]);

    private static List<string> SplitOps(string operands) {
        var outp = new List<string>();
        int depth = 0;
        int start = 0;
        for (int k = 0; k < operands.Length; k++) {
            char c = operands[k];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth--;
            } else if (c == ',' && depth == 0) {
                outp.Add(operands[start..k].Trim());
                start = k + 1;
            }
        }

        if (start < operands.Length) outp.Add(operands[start..].Trim());
        return outp;
    }

    private static bool TryImm(string token, out ulong value) {
        value = 0;
        string t = token.Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.EndsWith('!')) t = t[..^1];
        t = t.Trim();
        bool neg = t.StartsWith('-');
        if (neg) t = t[1..];
        bool ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (ok && neg) value = (ulong)-(long)value;
        return ok;
    }

    private static bool TryMem(string token, out string reg, out ulong off) {
        reg = "";
        off = 0;
        string t = token.Trim();
        int lb = t.IndexOf('[');
        int rb = t.IndexOf(']');
        if (lb < 0 || rb < 0 || rb < lb) return false;
        string inner = t[(lb + 1)..rb];
        var parts = SplitOps(inner);
        if (parts.Count == 0) return false;
        reg = parts[0].Trim();
        if (parts.Count >= 2) TryImm(parts[1], out off);
        return true;
    }

    private static int ShiftOf(IReadOnlyList<string> ops) {
        foreach (string o in ops) {
            string t = o.Trim();
            if (t.StartsWith("lsl", StringComparison.OrdinalIgnoreCase) &&
                TryImm(t[3..].TrimStart('#', ' '), out ulong s)) {
                return (int)s;
            }
        }

        return 0;
    }

    public readonly record struct BoostEntry(string Id, string? DisplayName, string? Description);

    public readonly record struct Result(bool Ok, IReadOnlyList<BoostEntry> Entries, string Diagnostics);
}
