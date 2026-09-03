using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class MachoFunctionStarts {
    private const uint MhMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagicLe = 0xBEBAFECA;
    private const uint CpuArm64 = 0x0100000C;
    private const uint LcFunctionStarts = 0x26;
    private const uint LcSegment64 = 0x19;

    public static IReadOnlyList<int> Read(byte[] bin) {
        var outp = new List<int>();
        if (bin is null || bin.Length < 32) return outp;
        try {
            uint magic = U32(bin, 0);
            int b = 0;
            if (magic is FatMagic or FatMagicLe) {
                if (!TryFatArm64(bin, out b) || b + 32 > bin.Length) return outp;
                magic = U32(bin, b);
            }

            if (magic != MhMagic64) return outp;

            uint ncmds = U32(bin, b + 16);
            int lc = b + 32;
            int dataoff = 0, datasize = 0;
            uint textFileOff = 0;
            bool haveText = false;

            for (uint c = 0; c < ncmds; c++) {
                if (lc + 8 > bin.Length) break;
                uint cmd = U32(bin, lc);
                uint cmdsize = U32(bin, lc + 4);
                if (cmdsize < 8 || lc + cmdsize > bin.Length) break;
                if (cmd == LcFunctionStarts) {
                    dataoff = (int)U32(bin, lc + 8) + b;
                    datasize = (int)U32(bin, lc + 12);
                } else if (cmd == LcSegment64 && Cstr16(bin, lc + 8) == "__TEXT") {
                    textFileOff = U32(bin, lc + 40);
                    haveText = true;
                }

                lc += (int)cmdsize;
            }

            if (datasize == 0 || dataoff <= 0 || dataoff + datasize > bin.Length || !haveText) return outp;

            long off = textFileOff;
            int p = dataoff;
            int end = dataoff + datasize;
            while (p < end) {
                ulong delta = ReadUleb(bin, ref p, end);
                if (delta == 0) break;
                off += (long)delta;
                if (off < 0 || off >= bin.Length) break;
                outp.Add((int)off);
            }
        } catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException
                                         or OverflowException) {
            return outp;
        }

        return outp;
    }

    public static bool TryEnclosingStart(byte[] bin, ulong targetVa, out ulong startVa, out ulong endVa) {
        startVa = 0;
        endVa = 0;
        if (!MachoText.TryFindText(bin, out int textFileOff, out int textSize, out ulong textVm)) return false;
        var starts = Read(bin);
        if (starts.Count == 0) return false;
        ulong slide = textVm - (ulong)textFileOff;
        ulong textEndVa = textVm + (ulong)textSize;

        ulong best = 0;
        bool have = false;
        ulong next = textEndVa;
        for (int i = 0; i < starts.Count; i++) {
            ulong va = (ulong)starts[i] + slide;
            if (va <= targetVa && va >= best) {
                best = va;
                have = true;
                next = i + 1 < starts.Count ? (ulong)starts[i + 1] + slide : textEndVa;
            }
        }

        if (!have) return false;
        startVa = best;
        endVa = next;
        return true;
    }

    private static ulong ReadUleb(byte[] b, ref int p, int end) {
        ulong result = 0;
        int shift = 0;
        while (p < end) {
            byte by = b[p++];
            result |= (ulong)(by & 0x7F) << shift;
            if ((by & 0x80) == 0) break;
            shift += 7;
            if (shift > 63) break;
        }

        return result;
    }

    private static bool TryFatArm64(byte[] b, out int offset) {
        offset = 0;
        if (b.Length < 8) return false;
        uint nfat = U32be(b, 4);
        int e = 8;
        for (uint i = 0; i < nfat; i++) {
            if (e + 20 > b.Length) return false;
            if (U32be(b, e) == CpuArm64) {
                offset = (int)U32be(b, e + 8);
                return true;
            }

            e += 20;
        }

        return false;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static uint U32be(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private static string Cstr16(byte[] b, int o) {
        int end = o, max = Math.Min(o + 16, b.Length);
        while (end < max && b[end] != 0) end++;
        return Encoding.ASCII.GetString(b, o, end - o);
    }
}
