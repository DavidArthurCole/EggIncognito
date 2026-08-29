namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64FieldWriteScanner {
    public readonly record struct Write(ulong Va, string Mnemonic, string BaseReg, long Offset, string Symbol);

    public readonly record struct Result(bool Ok, int Total, IReadOnlyList<Write> Writes, string Diagnostics);

    public static Result Scan(byte[] bin, long loOffset, long hiOffset, string? symbolFilter = null,
        bool includeStack = false, int max = 400) {
        if (bin is null || bin.Length < 64) return new Result(false, 0, [], "binary too short");
        var img = BinaryImage.Load(bin);
        if (img is null) return new Result(false, 0, [], "unrecognized binary format");
        if (!img.TryFindText(out int fo, out int size, out ulong vm))
            return new Result(false, 0, [], "no text section");
        if (fo < 0 || size <= 0 || (long)fo + size > bin.Length)
            return new Result(false, 0, [], "text section out of bounds");

        var index = MachoSymbols.Index.Build(img.Symbols);
        var hits = new List<Write>(Math.Min(max, 1024));
        int total = 0;

        for (int p = 0; p + 4 <= size; p += 4) {
            int b = fo + p;
            uint insn = (uint)(bin[b] | (bin[b + 1] << 8) | (bin[b + 2] << 16) | (bin[b + 3] << 24));
            if (!TryDecodeStore(insn, out string mnemonic, out int rn, out long off)) continue;
            if (!includeStack && rn == 31) continue;
            if (off < loOffset || off > hiOffset) continue;

            ulong va = vm + (ulong)p;
            string sym = index.NameOf(va);
            if (!string.IsNullOrEmpty(symbolFilter)
                && sym.IndexOf(symbolFilter, StringComparison.OrdinalIgnoreCase) < 0) {
                continue;
            }

            total++;
            if (hits.Count < max) hits.Add(new Write(va, mnemonic, RegName(rn), off, sym));
        }

        return new Result(true, total, hits, hits.Count < total ? $"truncated to {max} of {total}" : "ok");
    }

    private static bool TryDecodeStore(uint insn, out string mnemonic, out int rn, out long offset) {
        mnemonic = "";
        offset = 0;
        rn = (int)((insn >> 5) & 31);
        uint size = (insn >> 30) & 3;
        uint v = (insn >> 26) & 1;

        if ((insn & 0x3BC00000u) == 0x39000000u) {
            offset = (long)((insn >> 10) & 0xFFF) << (int)size;
            mnemonic = StoreName(size, v, false);
            return true;
        }

        if ((insn & 0xFFC00000u) == 0x3D800000u) {
            offset = (long)((insn >> 10) & 0xFFF) << 4;
            mnemonic = "str.q";
            return true;
        }

        if ((insn & 0x3B200C00u) == 0x38000000u) {
            uint opc = (insn >> 22) & 3;
            bool q = v == 1 && size == 0 && opc == 2;
            if (opc != 0 && !q) return false;
            offset = SignExtend((insn >> 12) & 0x1FF, 9);
            mnemonic = q ? "stur.q" : StoreName(size, v, true);
            return true;
        }

        if ((insn & 0x3BC00000u) == 0x29000000u) {
            int scale = v == 0 ? size == 0 ? 2 : 3 : size == 0 ? 2 : size == 1 ? 3 : 4;
            offset = SignExtend((insn >> 15) & 0x7F, 7) << scale;
            mnemonic = "stp";
            return true;
        }

        return false;
    }

    private static long SignExtend(uint value, int bits) {
        long v = value;
        long sign = 1L << (bits - 1);
        return (v ^ sign) - sign;
    }

    private static string StoreName(uint size, uint v, bool unscaled) {
        string b = unscaled ? "stur" : "str";
        if (v == 1) {
            return size switch {
                0 => b + ".b",
                1 => b + ".h",
                2 => b + ".s",
                _ => b + ".d"
            };
        }

        return size switch {
            0 => b + "b",
            1 => b + "h",
            2 => b + ".w",
            _ => b + ".x"
        };
    }

    private static string RegName(int n) => n switch {
        29 => "fp",
        30 => "lr",
        31 => "sp",
        _ => "x" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
