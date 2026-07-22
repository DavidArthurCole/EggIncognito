using System.Globalization;
using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class StructInitReader {
    public readonly record struct StructInit(ulong BaseVa, IReadOnlyDictionary<long, byte> Bytes, IReadOnlyDictionary<long, ulong> Pointers, IReadOnlyDictionary<long, ulong> Templates) {
        public bool TryTemplate(long offset, out ulong srcVa) => Templates.TryGetValue(offset, out srcVa);

        public bool TryFloat64(long offset, out double value) {
            if (TryReadBytes(offset, 8, out var raw)) {
                value = BitConverter.Int64BitsToDouble((long)raw);
                return true;
            }
            value = 0;
            return false;
        }

        public bool TryInt(long offset, int width, out long value) {
            if (TryReadBytes(offset, width, out var raw)) {
                value = width switch {
                    1 => (sbyte)raw,
                    2 => (short)raw,
                    4 => (int)raw,
                    _ => (long)raw,
                };
                return true;
            }
            value = 0;
            return false;
        }

        public bool TryPointer(long offset, out ulong va) => Pointers.TryGetValue(offset, out va);

        public string TryInlineString(long start, int maxLen = 32) {
            var bytes = new List<byte>(maxLen);
            for (var off = start; off < start + maxLen; off++) {
                if (!Bytes.TryGetValue(off, out var b) || b == 0) break;
                if (b is < 0x20 or > 0x7e) break;
                bytes.Add(b);
            }
            return Encoding.ASCII.GetString([.. bytes]);
        }

        public bool TryInlineStringComplete(long start, out string value, int maxLen = 32) {
            var bytes = new List<byte>(maxLen);
            for (var off = start; off < start + maxLen; off++) {
                if (!Bytes.TryGetValue(off, out var b)) { value = ""; return false; }
                if (b == 0) { value = Encoding.ASCII.GetString([.. bytes]); return bytes.Count > 0; }
                if (b is < 0x20 or > 0x7e) { value = ""; return false; }
                bytes.Add(b);
            }
            value = "";
            return false;
        }

        private bool TryReadBytes(long start, int width, out ulong value) {
            value = 0;
            for (var k = 0; k < width; k++) {
                if (!Bytes.TryGetValue(start + k, out var b)) return false;
                value |= (ulong)b << (k * 8);
            }
            return true;
        }
    }

    public readonly record struct Result(bool Ok, IReadOnlyList<StructInit> Structs, string Diagnostics);

    public static Result Read(byte[] bin, string initSymbol, int maxInstructions = 100_000)
        => ReadWith(bin, MachoSymbols.Read(bin), initSymbol, maxInstructions);

    public static Result ReadWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string initSymbol, int maxInstructions = 100_000) {
        var lst = Arm64DataTableReader.ListWith(bin, syms, [initSymbol], maxInstructions);
        if (!lst.Ok) return new(false, [], lst.Diagnostics);

        var sections = MachoSections.Read(bin);
        var page = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var imm = new Dictionary<string, (ulong Val, bool Wide)>(StringComparer.Ordinal);
        var vec = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var vecSrc = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var byteWrites = new Dictionary<ulong, Dictionary<long, byte>>();
        var ptrWrites = new Dictionary<ulong, Dictionary<long, ulong>>();
        var tplWrites = new Dictionary<ulong, Dictionary<long, ulong>>();

        byte[]? ReadVecFrom(ulong va, int width) {
            if (!MachoSections.TryVaToFileOffset(sections, va, out var fo, out _)) return null;
            if (fo < 0 || fo + width > bin.Length) return null;
            var buf = new byte[width];
            Array.Copy(bin, fo, buf, 0, width);
            return buf;
        }

        void StoreBytes(ulong baseVa, long off, byte[] raw, ulong? srcVa) {
            if (!byteWrites.TryGetValue(baseVa, out var bytes)) {
                bytes = [];
                byteWrites[baseVa] = bytes;
            }
            for (var k = 0; k < raw.Length; k++)
                bytes[off + k] = raw[k];

            if (srcVa is { } sv) {
                if (!tplWrites.TryGetValue(baseVa, out var tpls)) {
                    tpls = [];
                    tplWrites[baseVa] = tpls;
                }
                tpls[off] = sv;
            }
        }

        void Store(ulong baseVa, long off, int width, ulong raw, ulong? ptr) {
            if (!byteWrites.TryGetValue(baseVa, out var bytes)) {
                bytes = [];
                byteWrites[baseVa] = bytes;
            }
            for (var k = 0; k < width; k++)
                bytes[off + k] = (byte)(raw >> (k * 8));

            if (ptr is { } p) {
                if (!ptrWrites.TryGetValue(baseVa, out var ptrs)) {
                    ptrs = [];
                    ptrWrites[baseVa] = ptrs;
                }
                ptrs[off] = p;
            }
        }

        foreach (var i in lst.Instructions) {
            var ops = SplitOps(i.Operands);

            if (!TrackedProducers.Contains(i.Mnemonic) && !NonWritingMnemonics.Contains(i.Mnemonic)
                && ops.Count >= 1 && LooksLikeReg(ops[0])) {
                page.Remove(ops[0]);
                imm.Remove(RegNum(ops[0]));
            }

            if (ops.Count >= 1 && LooksLikeVecReg(ops[0]) && i.Mnemonic is not ("ldr" or "ldur" or "ldp")
                && !NonWritingMnemonics.Contains(i.Mnemonic)) {
                vec.Remove(ops[0]);
                vecSrc.Remove(ops[0]);
            }

            switch (i.Mnemonic) {
                case "bl":
                case "blr":
                    for (var r = 0; r <= 17; r++) {
                        page.Remove("x" + r);
                        page.Remove("w" + r);
                        imm.Remove(r.ToString(CultureInfo.InvariantCulture));
                    }
                    break;

                case "adrp":
                    if (ops.Count == 2 && TryImm(ops[1], out var pg)) {
                        page[ops[0]] = pg;
                        imm.Remove(RegNum(ops[0]));
                    }
                    break;

                case "add":
                    if (ops.Count == 3 && page.TryGetValue(ops[1], out var addBase) && TryImm(ops[2], out var addOff)) {
                        page[ops[0]] = addBase + addOff;
                        imm.Remove(RegNum(ops[0]));
                    } else if (ops.Count >= 1) {
                        page.Remove(ops[0]);
                        imm.Remove(RegNum(ops[0]));
                    }
                    break;

                case "mov":
                    if (ops.Count == 2) {
                        if (page.TryGetValue(ops[1], out var mv)) {
                            page[ops[0]] = mv;
                            imm.Remove(RegNum(ops[0]));
                        } else if (imm.TryGetValue(RegNum(ops[1]), out var mi)) {
                            imm[RegNum(ops[0])] = mi;
                            page.Remove(ops[0]);
                        } else if (TryImm(ops[1], out var mc)) {
                            imm[RegNum(ops[0])] = (mc, ops[0].StartsWith('x'));
                            page.Remove(ops[0]);
                        } else {
                            page.Remove(ops[0]);
                            imm.Remove(RegNum(ops[0]));
                        }
                    }
                    break;

                case "ldr":
                case "ldur":
                    if (ops.Count >= 2 && LooksLikeVecReg(ops[0]) && TryMem(ops[^1], out var ldQreg, out var ldQoff)
                        && page.TryGetValue(ldQreg, out var ldQbase)) {
                        var src = ldQbase + ldQoff;
                        var buf = ReadVecFrom(src, VecWidth(ops[0]));
                        if (buf is not null) { vec[ops[0]] = buf; vecSrc[ops[0]] = src; } else { vec.Remove(ops[0]); vecSrc.Remove(ops[0]); }
                    } else if (ops.Count >= 2 && !LooksLikeVecReg(ops[0]) && TryMem(ops[^1], out var ldMreg, out var ldMoff) && page.TryGetValue(ldMreg, out var ldMbase)) {
                        page[ops[0]] = ldMbase + ldMoff;
                        imm.Remove(RegNum(ops[0]));
                    } else if (ops.Count >= 1) {
                        page.Remove(ops[0]);
                        imm.Remove(RegNum(ops[0]));
                        vec.Remove(ops[0]);
                        vecSrc.Remove(ops[0]);
                    }
                    break;

                case "ldp":
                    if (ops.Count >= 3 && LooksLikeVecReg(ops[0]) && TryMem(ops[^1], out var ldpReg, out var ldpOff)
                        && page.TryGetValue(ldpReg, out var ldpBase)) {
                        var w = VecWidth(ops[0]);
                        var loSrc = ldpBase + ldpOff;
                        var hiSrc = ldpBase + ldpOff + (ulong)w;
                        var lo = ReadVecFrom(loSrc, w);
                        var hi = ReadVecFrom(hiSrc, w);
                        if (lo is not null) { vec[ops[0]] = lo; vecSrc[ops[0]] = loSrc; } else { vec.Remove(ops[0]); vecSrc.Remove(ops[0]); }
                        if (hi is not null) { vec[ops[1]] = hi; vecSrc[ops[1]] = hiSrc; } else { vec.Remove(ops[1]); vecSrc.Remove(ops[1]); }
                    }
                    break;

                case "movz":
                    if (ops.Count >= 2 && TryImm(ops[1], out var mz)) {
                        var shift = ShiftOf(ops);
                        var wide = ops[0].StartsWith('x');
                        imm[RegNum(ops[0])] = (mz << shift, wide);
                        page.Remove(ops[0]);
                    }
                    break;

                case "orr":
                    if (ops.Count == 3 && IsZeroReg(ops[1]) && TryImm(ops[2], out var orrImm)) {
                        imm[RegNum(ops[0])] = (orrImm, ops[0].StartsWith('x'));
                        page.Remove(ops[0]);
                    } else if (ops.Count >= 1) {
                        page.Remove(ops[0]);
                        imm.Remove(RegNum(ops[0]));
                    }
                    break;

                case "movn":
                    if (ops.Count >= 2 && TryImm(ops[1], out var mn)) {
                        var shift = ShiftOf(ops);
                        var wide = ops[0].StartsWith('x');
                        var raw = ~(mn << shift);
                        if (!wide) raw &= 0xFFFFFFFF;
                        imm[RegNum(ops[0])] = (raw, wide);
                        page.Remove(ops[0]);
                    }
                    break;

                case "movk":
                    if (ops.Count >= 2 && TryImm(ops[1], out var mk)) {
                        var shift = ShiftOf(ops);
                        var key = RegNum(ops[0]);
                        var prev = imm.TryGetValue(key, out var p) ? p : (0UL, ops[0].StartsWith('x'));
                        var val = (prev.Item1 & ~(0xFFFFUL << shift)) | (mk << shift);
                        imm[key] = (val, prev.Item2 || ops[0].StartsWith('x'));
                    }
                    break;

                case "str":
                case "stur":
                case "strb":
                case "sturb":
                case "strh":
                case "sturh":
                    if (ops.Count >= 2 && TryMem(ops[^1], out var sReg, out var sOffU) && page.TryGetValue(sReg, out var sBase)) {
                        var off = unchecked((long)sOffU);
                        var rt = ops[0];
                        if (LooksLikeVecReg(rt)) {
                            if (vec.TryGetValue(rt, out var vv))
                                StoreBytes(sBase, off, vv, vecSrc.TryGetValue(rt, out var sv) ? sv : null);
                        } else {
                            var width = i.Mnemonic switch { "strb" or "sturb" => 1, "strh" or "sturh" => 2, _ => rt.StartsWith('x') ? 8 : 4 };
                            if (imm.TryGetValue(RegNum(rt), out var iv))
                                Store(sBase, off, width, iv.Val, null);
                            else if (page.TryGetValue(rt, out var pv))
                                Store(sBase, off, width, pv, pv);
                        }
                    }
                    break;

                case "stp":
                    if (ops.Count >= 3 && TryMem(ops[^1], out var pReg, out var pOffU) && page.TryGetValue(pReg, out var pBase)) {
                        var off0 = unchecked((long)pOffU);
                        if (LooksLikeVecReg(ops[0])) {
                            var w = VecWidth(ops[0]);
                            if (vec.TryGetValue(ops[0], out var vv0))
                                StoreBytes(pBase, off0, vv0, vecSrc.TryGetValue(ops[0], out var sv0) ? sv0 : null);
                            if (vec.TryGetValue(ops[1], out var vv1))
                                StoreBytes(pBase, off0 + w, vv1, vecSrc.TryGetValue(ops[1], out var sv1) ? sv1 : null);
                        } else {
                            var w0 = ops[0].StartsWith('x') ? 8 : 4;
                            if (imm.TryGetValue(RegNum(ops[0]), out var iv0)) Store(pBase, off0, w0, iv0.Val, null);
                            if (imm.TryGetValue(RegNum(ops[1]), out var iv1)) Store(pBase, off0 + w0, w0, iv1.Val, null);
                        }
                    }
                    break;
            }
        }

        var emptyPtrs = new Dictionary<long, ulong>();
        var structs = byteWrites
            .OrderBy(kv => kv.Key)
            .Select(kv => new StructInit(kv.Key, kv.Value,
                ptrWrites.TryGetValue(kv.Key, out var p) ? p : emptyPtrs,
                tplWrites.TryGetValue(kv.Key, out var t) ? t : emptyPtrs))
            .ToList();
        return new(true, structs, $"{structs.Count} struct bases, {structs.Sum(s => s.Bytes.Count)} bytes");
    }

    private static List<string> SplitOps(string operands) {
        var outp = new List<string>();
        var depth = 0;
        var start = 0;
        for (var k = 0; k < operands.Length; k++) {
            var c = operands[k];
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
        var t = token.Trim();
        if (t.StartsWith('#')) t = t[1..];
        if (t.EndsWith('!')) t = t[..^1];
        t = t.Trim();
        var neg = t.StartsWith('-');
        if (neg) t = t[1..];
        var ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : ulong.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        if (ok && neg) value = (ulong)(-(long)value);
        return ok;
    }

    private static bool TryMem(string token, out string reg, out ulong off) {
        reg = "";
        off = 0;
        var t = token.Trim();
        var lb = t.IndexOf('[');
        var rb = t.IndexOf(']');
        if (lb < 0 || rb < 0 || rb < lb) return false;
        var inner = t[(lb + 1)..rb];
        var parts = SplitOps(inner);
        if (parts.Count == 0) return false;
        reg = parts[0].Trim();
        if (parts.Count >= 2) TryImm(parts[1], out off);
        return true;
    }

    private static int ShiftOf(IReadOnlyList<string> ops) {
        foreach (var o in ops) {
            var t = o.Trim();
            if (t.StartsWith("lsl", StringComparison.OrdinalIgnoreCase) && TryImm(t[3..].TrimStart('#', ' '), out var s))
                return (int)s;
        }
        return 0;
    }

    private static string RegNum(string reg) {
        var t = reg.Trim();
        return t.Length > 1 && (t[0] == 'w' || t[0] == 'x') ? t[1..] : t;
    }

    private static bool LooksLikeReg(string tok) => tok.Length >= 2 && (tok[0] == 'x' || tok[0] == 'w') && char.IsDigit(tok[1]);

    private static bool IsZeroReg(string tok) => tok is "wzr" or "xzr";

    private static bool LooksLikeVecReg(string tok) => tok.Length >= 2 && (tok[0] == 'q' || tok[0] == 'v' || tok[0] == 'd' || tok[0] == 's') && char.IsDigit(tok[1]);

    private static int VecWidth(string tok) => tok.Length >= 1 ? tok[0] switch { 'q' or 'v' => 16, 'd' => 8, 's' => 4, _ => 16 } : 16;

    private static readonly HashSet<string> TrackedProducers = [with(StringComparer.Ordinal), "adrp", "add", "mov", "ldr", "ldur", "ldp", "movz", "movk", "movn", "orr"];

    private static readonly HashSet<string> NonWritingMnemonics = [with(StringComparer.Ordinal), "cmp", "cmn", "tst", "fcmp", "str", "stur", "strb", "sturb", "strh", "sturh", "stp"];
}
