using System.Globalization;
using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class Arm64Signature {
    public static Pattern Build(byte[] bin, ulong startVa, ulong endVa, ulong textVmAddr, int textFileOff,
        int instructions) {
        if (!Arm64Decode.SliceFunction(bin, startVa, endVa, textVmAddr, textFileOff, out byte[] code, out _))
            return new Pattern(false, "", 0, 0, "function range out of bounds");

        int words = Math.Min(instructions, code.Length / 4);
        if (words < 2) return new Pattern(false, "", 0, 0, "function too short for a signature");

        var sb = new StringBuilder();
        int masked = 0;
        for (int i = 0; i < words; i++) {
            uint w = (uint)(code[i * 4] | (code[i * 4 + 1] << 8) | (code[i * 4 + 2] << 16) | (code[i * 4 + 3] << 24));
            bool isRel = IsPcRelative(w);
            if (isRel) masked++;

            for (int byteIdx = 0; byteIdx < 4; byteIdx++) {
                if (sb.Length > 0) sb.Append(' ');
                bool keep = !isRel || byteIdx == 3;
                if (keep) sb.Append(((byte)(w >> (byteIdx * 8))).ToString("x2", CultureInfo.InvariantCulture));
                else sb.Append("??");
            }
        }

        return new Pattern(true, sb.ToString(), words, masked, "ok");
    }

    private static bool IsPcRelative(uint w) {
        uint top6 = w >> 26;
        uint top8 = w >> 24;
        bool brImm = top6 is 0b000101 or 0b100101;
        bool bcond = top8 == 0b01010100;
        bool adr = (w & 0x1F000000) == 0x10000000;
        return brImm || bcond || adr;
    }

    public readonly record struct Pattern(
        bool Ok,
        string FridaPattern,
        int Instructions,
        int MaskedWords,
        string Diagnostics);
}
