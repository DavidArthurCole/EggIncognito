using System.Text;

namespace EggIncognito.Services.ProtoExtract;

// Builds a frida-style byte-scan pattern from a recovered function's prologue, with pc-relative displacement
// bytes wildcarded, so a relocated-but-shaped-the-same prologue still matches across an adjacent build (for
// a function whose body changed but whose prologue instruction shape is stable).
// Limits: a prologue that actually changed will not match, and a too-short/too-generic prologue may match
// many sites; the caller widens using the returned instruction count + uniqueness hint. Pure.
public static class Arm64Signature
{
    public readonly record struct Pattern(bool Ok, string FridaPattern, int Instructions, int MaskedWords, string Diagnostics);

    // Build a masked pattern from the first `instructions` 4-byte words of [startVa, endVa) in `bin`.
    public static Pattern Build(byte[] bin, ulong startVa, ulong endVa, ulong textVmAddr, int textFileOff, int instructions)
    {
        if (!Arm64Decode.SliceFunction(bin, startVa, endVa, textVmAddr, textFileOff, out var code, out _))
            return new(false, "", 0, 0, "function range out of bounds");

        int words = Math.Min(instructions, code.Length / 4);
        if (words < 2) return new(false, "", 0, 0, "function too short for a signature");

        var sb = new StringBuilder();
        int masked = 0;
        for (int i = 0; i < words; i++)
        {
            uint w = (uint)(code[i * 4] | (code[i * 4 + 1] << 8) | (code[i * 4 + 2] << 16) | (code[i * 4 + 3] << 24));
            bool isRel = IsPcRelative(w);
            if (isRel) masked++;
            // little-endian byte order in the pattern; mask the 3 displacement-bearing low bytes of a pc-rel word,
            // keep the opcode byte. Non-rel words are fully fixed.
            for (int byteIdx = 0; byteIdx < 4; byteIdx++)
            {
                if (sb.Length > 0) sb.Append(' ');
                bool keep = !isRel || byteIdx == 3; // top byte (opcode bits) kept for pc-rel
                if (keep) sb.Append(((byte)(w >> (byteIdx * 8))).ToString("x2"));
                else sb.Append("??");
            }
        }
        return new(true, sb.ToString(), words, masked, "ok");
    }

    // b / bl / b.cond / adr / adrp carry a pc-relative displacement that moves when the function relocates.
    private static bool IsPcRelative(uint w)
    {
        uint top6 = w >> 26;
        uint top8 = w >> 24;
        bool brImm = top6 == 0b000101 || top6 == 0b100101; // b / bl
        bool bcond = top8 == 0b01010100; // b.cond
        bool adr = (w & 0x1F000000) == 0x10000000; // adr / adrp family
        return brImm || bcond || adr;
    }
}
