using System.Globalization;
using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class ResearchCatalogExtractor {
    public const string InitSymbol = "__GLOBAL__sub_I_researchdata";
    public const string CommonEnumSymbol = "ResearchData9enumForId";
    public const string EpicEnumSymbol = "ResearchData13enumForEpicId";
    private const string ProviderGetterPrefix = "__ZN25GameDimensionProviderBase";
    private const int VtableInvokeSlot = 6;
    private const int NameOffset = 0x00;
    private const int IdOffset = 0x18;
    private const int TierOffset = 0x50;
    private const int DescOffset = 0x58;
    private const int HelpOffset = 0x70;
    private const int MaxLevelOffset = 0x88;
    private const int EffectVtableOffset = 0xb8;
    private const int SsoCapacity = 0x17;
    private const int ProviderFieldDelta = 8;

    public enum Combine {
        MulPlusOne,
        Add
    }

    public readonly record struct ResearchEntry(
        string Id,
        string? Name,
        string? Description,
        string? Help,
        bool Epic,
        int? MaxLevel,
        int? Tier,
        string? Dimension,
        bool DimensionIsInt,
        Combine? CombineMode,
        double? Magnitude,
        string? DecodeNote);

    public readonly record struct Result(bool Ok, IReadOnlyList<ResearchEntry> Entries, string Diagnostics);

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? [], img?.Sections ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms,
        IReadOnlyList<MachoSections.Section> sections) {
        var dims = ReadDimensionOffsets(bin, syms);
        if (dims.Count == 0) return new Result(false, [], "no GameDimensionProviderBase getters resolved");

        if (!TryReadEnumArray(bin, syms, CommonEnumSymbol, out ulong commonGlobal, out int commonCount,
                out int stride)) {
            return new Result(false, [], "could not resolve ResearchData::enumForId array global");
        }

        if (!TryReadEnumArray(bin, syms, EpicEnumSymbol, out ulong epicGlobal, out int epicCount, out int epicStride)) {
            return new Result(false, [], "could not resolve ResearchData::enumForEpicId array global");
        }

        if (stride != epicStride)
            return new Result(false, [], $"stride mismatch: common 0x{stride:x}, epic 0x{epicStride:x}");

        var lst = Arm64DataTableReader.ListWith(bin, syms, [InitSymbol], 100_000);
        if (!lst.Ok) return new Result(false, [], lst.Diagnostics);

        var walk = WalkInit(bin, sections, lst.Instructions);

        if (!walk.AddrStores.TryGetValue(commonGlobal, out ulong commonBase))
            return new Result(false, [], "init does not store the common research array base");
        if (!walk.AddrStores.TryGetValue(epicGlobal, out ulong epicBase))
            return new Result(false, [], "init does not store the epic research array base");

        var entries = new List<ResearchEntry>(commonCount + epicCount);
        int decoded = 0;
        var undecoded = new List<string>();
        var missingIds = new List<int>();
        for (int k = 0; k < commonCount + epicCount; k++) {
            bool epic = k >= commonCount;
            ulong recBase = epic
                ? epicBase + (ulong)((k - commonCount) * stride)
                : commonBase + (ulong)(k * stride);

            string? id = ReadStringField(walk, recBase + IdOffset);
            if (string.IsNullOrEmpty(id)) {
                missingIds.Add(k);
                continue;
            }

            string? name = ReadStringField(walk, recBase + NameOffset);
            string? desc = ReadStringField(walk, recBase + DescOffset);
            string? help = ReadStringField(walk, recBase + HelpOffset);
            if (desc is not null) desc = CleanMarkup(desc);
            if (help is not null) help = CleanMarkup(help);
            int? maxLevel = ReadIntField(walk, recBase + MaxLevelOffset);
            int? tier = ReadIntField(walk, recBase + TierOffset);

            string? dimension = null;
            bool dimInt = false;
            Combine? combine = null;
            double? magnitude = null;
            string? note;
            if (walk.AddrStores.TryGetValue(recBase + EffectVtableOffset, out ulong vt)) {
                var dec = DecodeInvoke(bin, sections, vt, dims);
                dimension = dec.Dimension;
                dimInt = dec.DimensionIsInt;
                combine = dec.CombineMode;
                magnitude = dec.Magnitude;
                note = dec.Note;
            } else {
                note = "effect vtable store not found";
            }

            if (dimension is not null && combine is not null && magnitude is not null) decoded++;
            else undecoded.Add(id);

            entries.Add(new ResearchEntry(id, name, desc, help, epic, maxLevel, tier, dimension, dimInt, combine,
                magnitude, note));
        }

        if (missingIds.Count > 0) {
            return new Result(false, entries,
                $"records without id: {string.Join(", ", missingIds)} of {commonCount}+{epicCount}");
        }

        string diag = $"{commonCount} common + {epicCount} epic, {decoded} effect-decoded"
                      + (undecoded.Count > 0 ? $", undecoded: {string.Join(", ", undecoded)}" : "");
        return new Result(true, entries, diag);
    }

    public readonly record struct DimensionField(string Name, bool IsInt);

    public static Dictionary<int, DimensionField> ReadDimensionOffsets(byte[] bin,
        IReadOnlyList<MachoSymbols.Symbol> syms) {
        var map = new Dictionary<int, DimensionField>();
        foreach (var sym in syms) {
            if (!sym.Name.StartsWith(ProviderGetterPrefix, StringComparison.Ordinal)) continue;
            if (!sym.Name.EndsWith("Ev", StringComparison.Ordinal)) continue;
            string rest = sym.Name[ProviderGetterPrefix.Length..];
            int d = 0;
            while (d < rest.Length && char.IsAsciiDigit(rest[d])) d++;
            if (d == 0 || !int.TryParse(rest[..d], NumberStyles.None, CultureInfo.InvariantCulture, out int len))
                continue;
            if (rest.Length != d + len + 2) continue;
            string name = rest.Substring(d, len);
            if (sym.Value == 0) continue;

            var body = Arm64DataTableReader.ListRange(bin, sym.Value, sym.Value + 4, 1);
            if (!body.Ok || body.Instructions.Count == 0) continue;
            var first = body.Instructions[0];
            if (first.Mnemonic != "ldr") continue;
            var ops = SplitOps(first.Operands);
            if (ops.Count < 2 || !TryMem(string.Join(", ", ops.Skip(1)), out string baseReg, out long disp) ||
                baseReg != "x0") {
                continue;
            }

            bool isInt = ops[0].StartsWith('w');
            if (!isInt && !ops[0].StartsWith('d')) continue;
            map.TryAdd((int)disp, new DimensionField(name, isInt));
        }

        return map;
    }

    private static bool TryReadEnumArray(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string needle,
        out ulong globalVa, out int count, out int stride) {
        globalVa = 0;
        count = 0;
        stride = 0;
        var lst = Arm64DataTableReader.ListWith(bin, syms, [needle], 256);
        if (!lst.Ok) return false;

        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var i in lst.Instructions) {
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "adrp" when ops.Count == 2 && TryImm(ops[1], out long pg):
                    page[ops[0]] = (ulong)pg;
                    break;
                case "ldr" when ops.Count >= 2 && globalVa == 0:
                    if (TryMem(string.Join(", ", ops.Skip(1)), out string baseReg, out long disp) &&
                        page.TryGetValue(baseReg, out ulong pv)) {
                        globalVa = pv + (ulong)disp;
                    }

                    break;
                case "add" when ops.Count == 3 && ops[0] == ops[1] && TryImm(ops[2], out long inc) && inc > 0x20:
                    if (stride == 0) stride = (int)inc;
                    break;
                case "cmp" when ops.Count == 2 && TryImm(ops[1], out long cmp) && cmp > 1:
                    if (count == 0) count = (int)cmp;
                    break;
            }
        }

        return globalVa != 0 && count > 0 && stride > 0;
    }

    private readonly record struct ByteWrite(int Order, byte[] Bytes);

    private sealed record InitWalk(
        Dictionary<ulong, ulong> AddrStores,
        Dictionary<ulong, long> ImmStores,
        Dictionary<ulong, List<ByteWrite>> AbsBytes,
        Dictionary<ulong, int> HeapAt,
        Dictionary<int, Dictionary<long, List<ByteWrite>>> TokBytes);

    private readonly record struct RegVal(char Kind, ulong Addr, long Imm, int Tok, long TokOff, ulong Src);

    private static InitWalk WalkInit(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        IReadOnlyList<Arm64DataTableReader.Insn> insns) {
        var regs = new Dictionary<string, RegVal>(StringComparer.Ordinal);
        var frame = new Dictionary<(string Base, long Off), ulong>();
        var addrStores = new Dictionary<ulong, ulong>();
        var immStores = new Dictionary<ulong, long>();
        var absBytes = new Dictionary<ulong, List<ByteWrite>>();
        var heapAt = new Dictionary<ulong, int>();
        var tokBytes = new Dictionary<int, Dictionary<long, List<ByteWrite>>>();
        int nextTok = 0;
        int order = 0;

        void Clobber(string reg) => regs.Remove(reg);

        void ClobberPair(string reg) {
            Clobber(reg);
            if (reg.Length >= 2 && reg[0] is 'w' or 'x') {
                Clobber((reg[0] == 'w' ? "x" : "w") + reg[1..]);
            }
        }

        RegVal? Get(string reg) {
            if (regs.TryGetValue(reg, out var v)) return v;
            if (reg.Length >= 2 && reg[0] == 'w' && regs.TryGetValue("x" + reg[1..], out var xv) && xv.Kind == 'i')
                return xv;
            if (reg is "wzr" or "xzr") return new RegVal('i', 0, 0, 0, 0, 0);
            return null;
        }

        void Set(string reg, RegVal v) {
            regs[reg] = v;
            if (reg.Length >= 2 && reg[0] is 'w' or 'x') regs.Remove((reg[0] == 'w' ? "x" : "w") + reg[1..]);
        }

        bool IsCstring(ulong va) =>
            MachoSections.TryVaToFileOffset(sections, va, out _, out var owner) && owner.Name == "__cstring";

        void WriteBytes(RegVal target, long extra, byte[] bytes) {
            if (target.Kind == 'a') {
                ulong t = target.Addr + (ulong)extra;
                if (!absBytes.TryGetValue(t, out var list)) absBytes[t] = list = [];
                list.Add(new ByteWrite(order, bytes));
            } else if (target.Kind == 't') {
                if (!tokBytes.TryGetValue(target.Tok, out var perOff)) tokBytes[target.Tok] = perOff = [];
                long t = target.TokOff + extra;
                if (!perOff.TryGetValue(t, out var list)) perOff[t] = list = [];
                list.Add(new ByteWrite(order, bytes));
            }
        }

        byte[] SrcBytes(ulong srcVa, int size) {
            if (!MachoSections.TryVaToFileOffset(sections, srcVa, out int fo, out _)) return [];
            int n = Math.Min(size, bin.Length - fo);
            if (n <= 0) return [];
            byte[] b = new byte[n];
            Array.Copy(bin, fo, b, 0, n);
            return b;
        }

        void DoStore(string rt, string memToken, bool writeback, string mnemonic) {
            if (!TryMem(memToken, out string baseReg, out long disp)) return;
            if (baseReg is "sp" or "x29") {
                if (Get(rt) is { Kind: 's' } spill) frame[(baseReg, disp)] = spill.Src;
                else frame.Remove((baseReg, disp));
                return;
            }

            var bv = Get(baseReg);
            if (bv is not { } b || (b.Kind != 'a' && b.Kind != 't')) return;

            var target = b;
            long off = disp;
            if (writeback) {
                target = b.Kind == 'a'
                    ? b with { Addr = b.Addr + (ulong)disp }
                    : b with { TokOff = b.TokOff + disp };
                Set(baseReg, target);
                off = 0;
            }

            int size = mnemonic is "strb" or "sturb" ? 1 :
                mnemonic is "strh" or "sturh" ? 2 :
                rt[0] switch {
                    'w' or 's' => 4,
                    'x' or 'd' => 8,
                    'q' => 16,
                    _ => 0
                };
            if (size == 0) return;

            var rv = Get(rt);
            if (rt is "xzr" or "wzr") rv = new RegVal('i', 0, 0, 0, 0, 0);

            ulong absTarget = target.Kind == 'a' ? target.Addr + (ulong)off : 0;

            if (rv is { } v) {
                switch (v.Kind) {
                    case 'i':
                        if (target.Kind == 'a' && size >= 4) immStores[absTarget] = v.Imm;
                        WriteBytes(target, off, BitConverter.GetBytes(v.Imm)[..Math.Min(size, 8)]);
                        return;
                    case 'a':
                        if (target.Kind == 'a' && size == 8) addrStores[absTarget] = v.Addr;
                        return;
                    case 's':
                        WriteBytes(target, off, SrcBytes(v.Src, size));
                        return;
                    case 't':
                        if (target.Kind == 'a' && size == 8 && v.TokOff == 0) heapAt[absTarget] = v.Tok;
                        return;
                }
            }
        }

        foreach (var i in insns) {
            order++;
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "adrp":
                    if (ops.Count == 2 && TryImm(ops[1], out long pg))
                        Set(ops[0], new RegVal('a', (ulong)pg, 0, 0, 0, 0));
                    else if (ops.Count >= 1) ClobberPair(ops[0]);
                    break;

                case "add":
                    if (ops.Count == 3 && TryImm(ops[2], out long addOff) && Get(ops[1]) is { } av) {
                        switch (av.Kind) {
                            case 'a':
                                Set(ops[0], av with { Addr = av.Addr + (ulong)addOff });
                                break;
                            case 't':
                                Set(ops[0], av with { TokOff = av.TokOff + addOff });
                                break;
                            case 'i':
                                Set(ops[0], av with { Imm = av.Imm + addOff });
                                break;
                            default:
                                ClobberPair(ops[0]);
                                break;
                        }
                    } else if (ops.Count >= 1) {
                        ClobberPair(ops[0]);
                    }

                    break;

                case "sub":
                    if (ops.Count == 3 && TryImm(ops[2], out long subOff) && Get(ops[1]) is { } sv) {
                        switch (sv.Kind) {
                            case 'a':
                                Set(ops[0], sv with { Addr = sv.Addr - (ulong)subOff });
                                break;
                            case 't':
                                Set(ops[0], sv with { TokOff = sv.TokOff - subOff });
                                break;
                            case 'i':
                                Set(ops[0], sv with { Imm = sv.Imm - subOff });
                                break;
                            default:
                                ClobberPair(ops[0]);
                                break;
                        }
                    } else if (ops.Count >= 1) {
                        ClobberPair(ops[0]);
                    }

                    break;

                case "mov":
                    if (ops.Count == 2) {
                        if (Get(ops[1]) is { } mv) Set(ops[0], mv);
                        else if (TryImm(ops[1], out long mi)) Set(ops[0], new RegVal('i', 0, mi, 0, 0, 0));
                        else ClobberPair(ops[0]);
                    }

                    break;

                case "movz":
                    if (ops.Count >= 2 && TryImm(ops[1], out long mz)) {
                        int shift = ops.Count == 3 ? ParseShift(ops[2]) : 0;
                        Set(ops[0], new RegVal('i', 0, mz << shift, 0, 0, 0));
                    } else if (ops.Count >= 1) {
                        ClobberPair(ops[0]);
                    }

                    break;

                case "movk":
                    if (ops.Count >= 2 && TryImm(ops[1], out long mk) && Get(ops[0]) is { Kind: 'i' } kv) {
                        int shift = ops.Count == 3 ? ParseShift(ops[2]) : 0;
                        long cleared = kv.Imm & ~(0xFFFFL << shift);
                        Set(ops[0], kv with { Imm = cleared | (mk << shift) });
                    } else if (ops.Count >= 1) {
                        ClobberPair(ops[0]);
                    }

                    break;

                case "orr":
                    if (ops.Count == 3 && ops[1] is "xzr" or "wzr" && TryImm(ops[2], out long ov))
                        Set(ops[0], new RegVal('i', 0, ov, 0, 0, 0));
                    else if (ops.Count >= 1) ClobberPair(ops[0]);
                    break;

                case "bl":
                case "blr":
                    for (int r = 0; r <= 17; r++) {
                        Clobber("x" + r);
                        Clobber("w" + r);
                    }

                    for (int r = 0; r <= 7; r++) {
                        Clobber("q" + r);
                        Clobber("d" + r);
                        Clobber("s" + r);
                    }

                    Set("x0", new RegVal('t', 0, 0, nextTok++, 0, 0));
                    break;

                case "ldr":
                case "ldur":
                    if (ops.Count >= 2 && TryMem(string.Join(", ", ops.Skip(1)), out string lb, out long ldisp)) {
                        if (lb is "sp" or "x29" && frame.TryGetValue((lb, ldisp), out ulong spilled)) {
                            Set(ops[0], new RegVal('s', 0, 0, 0, 0, spilled));
                            break;
                        }

                        if (Get(lb) is { Kind: 'a' } lav) {
                            ulong src = lav.Addr + (ulong)ldisp;
                            if (IsCstring(src)) {
                                Set(ops[0], new RegVal('s', 0, 0, 0, 0, src));
                                break;
                            }
                        }
                    }

                    if (ops.Count >= 1) ClobberPair(ops[0]);
                    break;

                case "ldp":
                    if (ops.Count >= 3 && TryMem(string.Join(", ", ops.Skip(2)), out string pb, out long pdisp) &&
                        Get(pb) is { Kind: 'a' } pav) {
                        ulong src = pav.Addr + (ulong)pdisp;
                        if (IsCstring(src)) {
                            Set(ops[0], new RegVal('s', 0, 0, 0, 0, src));
                            Set(ops[1], new RegVal('s', 0, 0, 0, 0, src + 16));
                            break;
                        }
                    }

                    if (ops.Count >= 1) ClobberPair(ops[0]);
                    if (ops.Count >= 2) ClobberPair(ops[1]);
                    break;

                case "str":
                case "stur":
                case "strb":
                case "sturb":
                case "strh":
                case "sturh":
                    if (ops.Count >= 2) {
                        DoStore(ops[0], string.Join(", ", ops.Skip(1)), i.Operands.TrimEnd().EndsWith('!'),
                            i.Mnemonic);
                    }

                    break;

                case "stp":
                    if (ops.Count >= 3 && ops[0][0] is 'q' or 'x' or 'w' or 'd' &&
                        TryMem(string.Join(", ", ops.Skip(2)), out _, out _)) {
                        string mem = string.Join(", ", ops.Skip(2));
                        bool wb = i.Operands.TrimEnd().EndsWith('!');
                        int elem = ops[0][0] == 'q' ? 16 : ops[0][0] == 'w' ? 4 : 8;
                        DoStore(ops[0], mem, wb, "str");
                        if (TryMem(mem, out string spb, out long spDisp) && Get(spb) is { } spv &&
                            (spv.Kind == 'a' || spv.Kind == 't')) {
                            long second = (wb ? 0 : spDisp) + elem;
                            var rv2 = ops[1] is "xzr" or "wzr" ? new RegVal('i', 0, 0, 0, 0, 0) : Get(ops[1]);
                            if (rv2 is { Kind: 's' } sreg) {
                                WriteBytes(spv, second, SrcBytes(sreg.Src, elem));
                            } else if (rv2 is { Kind: 'i' } ireg) {
                                WriteBytes(spv, second, BitConverter.GetBytes(ireg.Imm)[..Math.Min(elem, 8)]);
                                if (spv.Kind == 'a' && elem >= 4)
                                    immStores[spv.Addr + (ulong)second] = ireg.Imm;
                            } else if (rv2 is { Kind: 'a' } areg && spv.Kind == 'a' && elem == 8) {
                                addrStores[spv.Addr + (ulong)second] = areg.Addr;
                            }
                        }
                    }

                    break;
            }
        }

        return new InitWalk(addrStores, immStores, absBytes, heapAt, tokBytes);
    }

    private static string? ReadStringField(InitWalk walk, ulong fieldVa) {
        if (walk.HeapAt.TryGetValue(fieldVa, out int tok)) {
            if (!walk.TokBytes.TryGetValue(tok, out var perOff)) return null;
            return AssembleString(perOff.Select(kv => (kv.Key, kv.Value)));
        }

        var writes = new List<(long Off, List<ByteWrite> W)>();
        for (int o = 0; o <= SsoCapacity; o++) {
            if (walk.AbsBytes.TryGetValue(fieldVa + (ulong)o, out var list)) writes.Add((o, list));
        }

        return writes.Count == 0 ? null : AssembleString(writes.Select(w => (w.Off, w.W)));
    }

    private static string? AssembleString(IEnumerable<(long Off, List<ByteWrite> Writes)> writes) {
        const int cap = 512;
        byte[] buf = new byte[cap];
        bool[] set = new bool[cap];
        var ordered = writes
            .SelectMany(w => w.Writes.Select(bw => (w.Off, bw.Order, bw.Bytes)))
            .OrderBy(t => t.Order);
        foreach ((long off, _, byte[] bytes) in ordered) {
            for (int j = 0; j < bytes.Length; j++) {
                long p = off + j;
                if (p is < 0 or >= cap) continue;
                buf[p] = bytes[j];
                set[p] = true;
            }
        }

        int end = 0;
        while (end < cap && set[end] && buf[end] != 0) end++;
        if (end == 0) return null;
        return Encoding.UTF8.GetString(buf, 0, end);
    }

    private static int? ReadIntField(InitWalk walk, ulong fieldVa)
        => walk.ImmStores.TryGetValue(fieldVa, out long v) ? (int)v : null;

    private static string CleanMarkup(string s) {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++) {
            if (s[i] == '\x1b') {
                i++;
                continue;
            }

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    private readonly record struct InvokeDecode(
        string? Dimension,
        bool DimensionIsInt,
        Combine? CombineMode,
        double? Magnitude,
        string? Note);

    private enum SymKind {
        Level,
        Const,
        ConstPair,
        One,
        Cur,
        CurVec,
        OnePlus,
        OnePlusPair,
        CurPlus,
        CurTimesOnePlus,
        CurTimesOnePlusVec,
        SetFromLevel,
        CallOnLevel,
        CurTimesCall
    }

    private readonly record struct SymVal(SymKind Kind, double A = 0, double B = 0, int Off = 0);

    private readonly record struct EffectHit(int D, bool Vec, bool Int, char Kind, double Lo, double Hi);

    private static InvokeDecode DecodeInvoke(byte[] bin, IReadOnlyList<MachoSections.Section> sections,
        ulong vtableVa, Dictionary<int, DimensionField> dims) {
        if (!TryReadU64(bin, sections, vtableVa + (ulong)(VtableInvokeSlot * 8), out ulong invokeVa))
            return new InvokeDecode(null, false, null, null, "vtable slot unreadable");
        if (!MachoSections.TryVaToFileOffset(sections, invokeVa, out _, out var owner) || owner.Name != "__text")
            return new InvokeDecode(null, false, null, null, $"invoke 0x{invokeVa:x} not in __text");

        var lst = Arm64DataTableReader.ListRange(bin, invokeVa, invokeVa + 60 * 4, 60);
        if (!lst.Ok || lst.Instructions.Count == 0)
            return new InvokeDecode(null, false, null, null, "invoke disasm failed");
        if (lst.Instructions[0].Mnemonic == "ret")
            return new InvokeDecode(null, false, null, null, "no-op effect (bare ret)");

        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var xImm = new Dictionary<string, long>(StringComparer.Ordinal);
        var lvlInt = new Dictionary<string, double>(StringComparer.Ordinal);
        var curInt = new Dictionary<string, int>(StringComparer.Ordinal);
        var intRes = new Dictionary<string, (int D, double Scale)>(StringComparer.Ordinal);
        var fpv = new Dictionary<int, SymVal>();
        var prov = new HashSet<string>(StringComparer.Ordinal) { "x1" };
        var effects = new List<EffectHit>();
        bool anomaly = false;

        foreach (var i in lst.Instructions) {
            if (i.Mnemonic == "ret") break;
            var ops = SplitOps(i.Operands);
            switch (i.Mnemonic) {
                case "adrp" when ops.Count == 2 && TryImm(ops[1], out long pg):
                    page[ops[0]] = (ulong)pg;
                    break;

                case "mov" when ops.Count == 2:
                    if (ops[0][0] == 'x' && prov.Contains(ops[1])) prov.Add(ops[0]);
                    else if (xImm.TryGetValue(ops[1], out long xv)) xImm[ops[0]] = xv;
                    else xImm.Remove(ops[0]);
                    break;

                case "movz" when ops.Count >= 2 && TryImm(ops[1], out long mz):
                    xImm[ops[0]] = mz << (ops.Count == 3 ? ParseShift(ops[2]) : 0);
                    break;

                case "movk" when ops.Count >= 2 && TryImm(ops[1], out long mk) && xImm.ContainsKey(ops[0]): {
                        int shift = ops.Count == 3 ? ParseShift(ops[2]) : 0;
                        xImm[ops[0]] = (xImm[ops[0]] & ~(0xFFFFL << shift)) | (mk << shift);
                        break;
                    }

                case "orr" when ops.Count == 3 && ops[1] is "xzr" or "wzr" && TryImm(ops[2], out long ov):
                    xImm[ops[0]] = ov;
                    break;

                case "lsl" when ops.Count == 3 && TryImm(ops[2], out long lk):
                    if (lvlInt.TryGetValue(ops[1], out double ls)) lvlInt[ops[0]] = ls * (1L << (int)lk);
                    else lvlInt.Remove(ops[0]);
                    break;

                case "add" when ops.Count == 4 && ops[1] == ops[2] && lvlInt.TryGetValue(ops[1], out double as4) &&
                                ops[3].StartsWith("lsl", StringComparison.OrdinalIgnoreCase): {
                        int hash = ops[3].IndexOf('#');
                        if (hash >= 0 && int.TryParse(ops[3][(hash + 1)..], out int k))
                            lvlInt[ops[0]] = as4 * (1 + (1L << k));
                        break;
                    }

                case "add" when ops.Count == 3 && ops[0][0] == 'w': {
                        bool aLvl = lvlInt.ContainsKey(ops[1]);
                        bool bLvl = lvlInt.ContainsKey(ops[2]);
                        string other = aLvl ? ops[2] : ops[1];
                        if ((aLvl || bLvl) && curInt.TryGetValue(other, out int cd)) {
                            intRes[ops[0]] = (cd, lvlInt[aLvl ? ops[1] : ops[2]]);
                            curInt.Remove(ops[0]);
                            lvlInt.Remove(ops[0]);
                        } else {
                            lvlInt.Remove(ops[0]);
                            curInt.Remove(ops[0]);
                            intRes.Remove(ops[0]);
                        }

                        break;
                    }

                case "mul" when ops.Count == 3: {
                        bool aLvl = lvlInt.TryGetValue(ops[1], out double ms);
                        bool bLvl = lvlInt.TryGetValue(ops[2], out double ms2);
                        string other = aLvl ? ops[2] : ops[1];
                        if ((aLvl || bLvl) && xImm.TryGetValue(other, out long factor))
                            lvlInt[ops[0]] = (aLvl ? ms : ms2) * factor;
                        else lvlInt.Remove(ops[0]);
                        break;
                    }

                case "madd" when ops.Count == 4: {
                        bool aLvl = lvlInt.TryGetValue(ops[1], out double ds);
                        bool bLvl = lvlInt.TryGetValue(ops[2], out double ds2);
                        string other = aLvl ? ops[2] : ops[1];
                        if ((aLvl || bLvl) && xImm.TryGetValue(other, out long f) &&
                            curInt.TryGetValue(ops[3], out int cd)) {
                            intRes[ops[0]] = (cd, (aLvl ? ds : ds2) * f);
                        } else {
                            lvlInt.Remove(ops[0]);
                            curInt.Remove(ops[0]);
                            intRes.Remove(ops[0]);
                        }

                        break;
                    }

                case "ldr" or "ldur" when ops.Count >= 2 &&
                                          TryMem(string.Join(", ", ops.Skip(1)), out string mb, out long disp): {
                        char rk = ops[0][0];
                        int fn = FpNum(ops[0]);
                        if (mb == "x2") {
                            if (rk == 'w') lvlInt[ops[0]] = 1;
                            else if (fn >= 0) fpv[fn] = new SymVal(SymKind.Level, 1);
                        } else if (prov.Contains(mb)) {
                            if (rk == 'w') curInt[ops[0]] = (int)disp;
                            else if (rk == 'd' && fn >= 0) fpv[fn] = new SymVal(SymKind.Cur, 0, 0, (int)disp);
                            else if (rk == 'q' && fn >= 0) fpv[fn] = new SymVal(SymKind.CurVec, 0, 0, (int)disp);
                        } else if (page.TryGetValue(mb, out ulong pv) && fn >= 0) {
                            ulong cva = pv + (ulong)disp;
                            if (rk == 'd' && TryReadF64(bin, sections, cva, out double dv)) {
                                fpv[fn] = new SymVal(SymKind.Const, dv);
                            } else if (rk == 's' && TryReadF32(bin, sections, cva, out float fv)) {
                                fpv[fn] = new SymVal(SymKind.Const, fv);
                            } else if (rk == 'q' && TryReadF64(bin, sections, cva, out double lo) &&
                                       TryReadF64(bin, sections, cva + 8, out double hi)) {
                                fpv[fn] = new SymVal(SymKind.ConstPair, lo, hi);
                            } else {
                                fpv.Remove(fn);
                            }
                        } else if (fn >= 0) {
                            fpv.Remove(fn);
                        } else if (rk is 'w' or 'x') {
                            lvlInt.Remove(ops[0]);
                            curInt.Remove(ops[0]);
                            xImm.Remove(ops[0]);
                        }

                        break;
                    }

                case "ucvtf" or "scvtf" when ops.Count == 2: {
                        int fd = FpNum(ops[0]);
                        if (fd < 0) break;
                        if (lvlInt.TryGetValue(ops[1], out double us)) {
                            fpv[fd] = new SymVal(SymKind.Level, us);
                        } else if (FpNum(ops[1]) is var fs && fs >= 0 && fpv.TryGetValue(fs, out var uv) &&
                                   uv.Kind == SymKind.Level) {
                            fpv[fd] = uv;
                        } else {
                            fpv.Remove(fd);
                        }

                        break;
                    }

                case "fmov" when ops.Count == 2: {
                        int fd = FpNum(ops[0]);
                        if (fd < 0) break;
                        bool vec = ops[0].StartsWith('v');
                        if (TryFpImm(ops[1], out double fi)) {
                            fpv[fd] = vec && Math.Abs(fi - 1.0) < 1e-12 ? new SymVal(SymKind.One)
                                : vec ? new SymVal(SymKind.ConstPair, fi, fi)
                                : Math.Abs(fi - 1.0) < 1e-12 ? new SymVal(SymKind.One)
                                : new SymVal(SymKind.Const, fi);
                        } else if (xImm.TryGetValue(ops[1], out long bits)) {
                            fpv[fd] = new SymVal(SymKind.Const, BitConverter.Int64BitsToDouble(bits));
                        } else {
                            fpv.Remove(fd);
                        }

                        break;
                    }

                case "fmadd" when ops.Count == 4: {
                        int fd = FpNum(ops[0]);
                        var a = FpVal(fpv, ops[1]);
                        var b = FpVal(fpv, ops[2]);
                        var c = FpVal(fpv, ops[3]);
                        if (fd < 0) break;
                        var lvl = Pick(a, b, SymKind.Level);
                        var cst = Pick(a, b, SymKind.Const);
                        if (lvl is { } lv && cst is { } cv) {
                            double m = lv.A * cv.A;
                            if (c is { Kind: SymKind.One }) fpv[fd] = new SymVal(SymKind.OnePlus, m);
                            else if (c is { Kind: SymKind.Cur } cc) fpv[fd] = new SymVal(SymKind.CurPlus, m, 0, cc.Off);
                            else fpv.Remove(fd);
                        } else {
                            fpv.Remove(fd);
                        }

                        break;
                    }

                case "fmla" when ops.Count == 3: {
                        int fd = FpNum(ops[0]);
                        var acc = FpVal(fpv, ops[0]);
                        var a = FpVal(fpv, ops[1]);
                        var b = FpVal(fpv, ops[2]);
                        if (fd < 0) break;
                        var pair = Pick(a, b, SymKind.ConstPair);
                        var lvl = Pick(a, b, SymKind.Level);
                        if (acc is { Kind: SymKind.One } && pair is { } pp && lvl is { } lv2)
                            fpv[fd] = new SymVal(SymKind.OnePlusPair, pp.A * lv2.A, pp.B * lv2.A);
                        else fpv.Remove(fd);
                        break;
                    }

                case "fadd" when ops.Count == 3: {
                        int fd = FpNum(ops[0]);
                        var a = FpVal(fpv, ops[1]);
                        var b = FpVal(fpv, ops[2]);
                        if (fd < 0) break;
                        if (ops[1] == ops[2] && a is { Kind: SymKind.Cur } self) {
                            fpv[fd] = new SymVal(SymKind.CurTimesOnePlus, 1.0, 0, self.Off);
                        } else if (Pick(a, b, SymKind.Cur) is { } cur) {
                            var otherVal = a is { Kind: SymKind.Cur } ? b : a;
                            if (otherVal is { Kind: SymKind.Level } lv3)
                                fpv[fd] = new SymVal(SymKind.CurPlus, lv3.A, 0, cur.Off);
                            else if (otherVal is { Kind: SymKind.Const } cv2)
                                fpv[fd] = new SymVal(SymKind.CurPlus, cv2.A, 0, cur.Off);
                            else fpv.Remove(fd);
                        } else {
                            fpv.Remove(fd);
                        }

                        break;
                    }

                case "fmul" when ops.Count == 3: {
                        int fd = FpNum(ops[0]);
                        var a = FpVal(fpv, ops[1]);
                        var b = FpVal(fpv, ops[2]);
                        if (fd < 0) break;
                        if (Pick(a, b, SymKind.Cur) is { } cur) {
                            var other = a is { Kind: SymKind.Cur } ? b : a;
                            fpv[fd] = other switch {
                                { Kind: SymKind.OnePlus } op2 => new SymVal(SymKind.CurTimesOnePlus, op2.A, 0, cur.Off),
                                { Kind: SymKind.Const } cv3 => new SymVal(SymKind.CurTimesOnePlus, cv3.A - 1.0, 0,
                                    cur.Off),
                                { Kind: SymKind.CallOnLevel } => new SymVal(SymKind.CurTimesCall, 0, 0, cur.Off),
                                _ => new SymVal(SymKind.Level, 0)
                            };
                            if (other is not ({ Kind: SymKind.OnePlus } or { Kind: SymKind.Const }
                                or { Kind: SymKind.CallOnLevel })) {
                                fpv.Remove(fd);
                            }
                        } else if (Pick(a, b, SymKind.CurVec) is { } curv) {
                            var other = a is { Kind: SymKind.CurVec } ? b : a;
                            if (other is { Kind: SymKind.OnePlus } ops2) {
                                fpv[fd] = new SymVal(SymKind.CurTimesOnePlusVec, ops2.A, ops2.A, curv.Off);
                            } else if (other is { Kind: SymKind.OnePlusPair } opp) {
                                fpv[fd] = new SymVal(SymKind.CurTimesOnePlusVec, opp.A, opp.B, curv.Off);
                            } else {
                                fpv.Remove(fd);
                            }
                        } else if (Pick(a, b, SymKind.Level) is { } lvl4 &&
                                   Pick(a, b, SymKind.Const) is { } cst4) {
                            fpv[fd] = new SymVal(SymKind.SetFromLevel, lvl4.A * cst4.A);
                        } else {
                            fpv.Remove(fd);
                        }

                        break;
                    }

                case "bl" or "blr": {
                        bool levelIn = fpv.TryGetValue(0, out var d0) && d0.Kind == SymKind.Level;
                        fpv.Remove(0);
                        if (levelIn) fpv[0] = new SymVal(SymKind.CallOnLevel);
                        for (int r = 0; r <= 17; r++) {
                            xImm.Remove("x" + r);
                            xImm.Remove("w" + r);
                        }

                        break;
                    }

                case "str" or "stur" when ops.Count >= 2 &&
                                          TryMem(string.Join(", ", ops.Skip(1)), out string sb2, out long sd2): {
                        if (!prov.Contains(sb2)) break;
                        int d = (int)sd2;
                        char rk = ops[0][0];
                        if (rk == 'w') {
                            if (intRes.TryGetValue(ops[0], out var ir) && ir.D == d) {
                                effects.Add(new EffectHit(d, false, true, 'a', ir.Scale, 0));
                            } else {
                                anomaly = true;
                            }
                        } else if (rk is 'd' or 'q') {
                            int fn2 = FpNum(ops[0]);
                            var v = fn2 >= 0 && fpv.TryGetValue(fn2, out var vv) ? vv : default(SymVal?);
                            switch (v) {
                                case { Kind: SymKind.CurTimesOnePlus } m when m.Off == d:
                                    effects.Add(new EffectHit(d, false, false, 'm', m.A, 0));
                                    break;
                                case { Kind: SymKind.CurPlus } p when p.Off == d:
                                    effects.Add(new EffectHit(d, false, false, 'a', p.A, 0));
                                    break;
                                case { Kind: SymKind.SetFromLevel } s2:
                                    effects.Add(new EffectHit(d, false, false, 'a', s2.A, 0));
                                    break;
                                case { Kind: SymKind.CurTimesOnePlusVec } mv2 when mv2.Off == d:
                                    effects.Add(new EffectHit(d, true, false, 'm', mv2.A, mv2.B));
                                    break;
                                case { Kind: SymKind.CurTimesCall } cc2 when cc2.Off == d:
                                    effects.Add(new EffectHit(d, false, false, 'c', 0, 0));
                                    break;
                                default:
                                    anomaly = true;
                                    break;
                            }
                        }

                        break;
                    }
            }
        }

        return Summarize(effects, anomaly, dims, invokeVa);
    }

    private static InvokeDecode Summarize(List<EffectHit> effects, bool anomaly,
        Dictionary<int, DimensionField> dims, ulong invokeVa) {
        string DimName(int providerOff) =>
            dims.TryGetValue(providerOff, out var f) ? f.Name : $"+0x{providerOff:x}";

        if (effects.Count == 0) {
            return new InvokeDecode(null, false, null, null,
                anomaly ? $"unrecognized effect pattern @0x{invokeVa:x}" : $"no effect store found @0x{invokeVa:x}");
        }

        if (effects.Any(e => e.Kind == 'c')) {
            var e = effects[0];
            return new InvokeDecode(null, false, null, null,
                $"compounding per-level multiplier on {DimName(e.D + ProviderFieldDelta)} via runtime call @0x{invokeVa:x}; not representable as a linear row");
        }

        if (effects.Count > 1 || effects.Any(e => e.Vec) || anomaly) {
            var parts = new List<string>();
            foreach (var e in effects) {
                if (e.Vec) {
                    parts.Add(Describe(DimName(e.D + ProviderFieldDelta), e.Kind, e.Lo));
                    parts.Add(Describe(DimName(e.D + ProviderFieldDelta + 8), e.Kind, e.Hi));
                } else {
                    parts.Add(Describe(DimName(e.D + ProviderFieldDelta), e.Kind, e.Lo));
                }
            }

            string suffix = anomaly ? "; plus unrecognized stores" : "";
            return new InvokeDecode(null, false, null, null,
                $"multi-dimension effect: {string.Join(", ", parts)}{suffix} @0x{invokeVa:x}");
        }

        var only = effects[0];
        if (!dims.TryGetValue(only.D + ProviderFieldDelta, out var dim)) {
            return new InvokeDecode(null, false, null, null,
                $"provider offset 0x{only.D + ProviderFieldDelta:x} unmapped @0x{invokeVa:x}");
        }

        return new InvokeDecode(dim.Name, dim.IsInt, only.Kind == 'm' ? Combine.MulPlusOne : Combine.Add, only.Lo,
            null);
    }

    private static string Describe(string dim, char kind, double mag) =>
        kind == 'm' ? $"{dim} x(1{(mag >= 0 ? "+" : "")}{mag}/level)" : $"{dim} {(mag >= 0 ? "+" : "")}{mag}/level";

    private static SymVal? FpVal(Dictionary<int, SymVal> fpv, string reg) {
        int n = FpNum(reg);
        return n >= 0 && fpv.TryGetValue(n, out var v) ? v : null;
    }

    private static SymVal? Pick(SymVal? a, SymVal? b, SymKind kind) {
        if (a is { } av && av.Kind == kind) return av;
        if (b is { } bv && bv.Kind == kind) return bv;
        return null;
    }

    private static int FpNum(string reg) {
        if (reg.Length < 2 || reg[0] is not ('d' or 's' or 'q' or 'v')) return -1;
        int end = 1;
        while (end < reg.Length && char.IsAsciiDigit(reg[end])) end++;
        if (end == 1) return -1;
        return int.TryParse(reg[1..end], NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : -1;
    }

    private static bool TryReadU64(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va,
        out ulong value) {
        value = 0;
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out _) || fo + 8 > bin.Length) return false;
        value = BitConverter.ToUInt64(bin, fo);
        return true;
    }

    private static bool TryReadF64(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va,
        out double value) {
        value = 0;
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out _) || fo + 8 > bin.Length) return false;
        value = BitConverter.ToDouble(bin, fo);
        return double.IsFinite(value);
    }

    private static bool TryReadF32(byte[] bin, IReadOnlyList<MachoSections.Section> sections, ulong va,
        out float value) {
        value = 0;
        if (!MachoSections.TryVaToFileOffset(sections, va, out int fo, out _) || fo + 4 > bin.Length) return false;
        value = BitConverter.ToSingle(bin, fo);
        return float.IsFinite(value);
    }

    private static List<string> SplitOps(string operands) {
        var result = new List<string>();
        int depth = 0;
        int start = 0;
        for (int i = 0; i < operands.Length; i++) {
            char c = operands[i];
            if (c == '[') {
                depth++;
            } else if (c == ']') {
                depth--;
            } else if (c == ',' && depth == 0) {
                result.Add(operands[start..i].Trim());
                start = i + 1;
            }
        }

        if (start < operands.Length) result.Add(operands[start..].Trim());
        return result;
    }

    private static bool TryImm(string op, out long value) {
        value = 0;
        if (!op.StartsWith('#')) return false;
        string s = op[1..];
        bool neg = s.StartsWith('-');
        if (neg) s = s[1..];
        bool ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        if (neg) value = -value;
        return ok;
    }

    private static bool TryFpImm(string op, out double value) {
        value = 0;
        return op.StartsWith('#') &&
               double.TryParse(op[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static int ParseShift(string op) {
        string t = op.Trim();
        if (!t.StartsWith("lsl", StringComparison.OrdinalIgnoreCase)) return 0;
        int hash = t.IndexOf('#');
        return hash >= 0 && int.TryParse(t[(hash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture,
            out int s)
            ? s
            : 0;
    }

    private static bool TryMem(string mem, out string baseReg, out long disp) {
        baseReg = "";
        disp = 0;
        string t = mem.Trim().TrimEnd('!').Trim();
        if (!t.StartsWith('[') || !t.EndsWith(']')) return false;
        string inner = t[1..^1];
        var parts = inner.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        baseReg = parts[0];
        if (parts.Length == 1) return true;
        return parts.Length == 2 && TryImm(parts[1], out disp);
    }
}
