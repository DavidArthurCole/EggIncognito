namespace EggIncognito.Services.ProtoExtract;

// Minimal ARM64 instruction decoder. Decodes ONLY the three encodings the clientVersion heuristic
// needs: MOVZ / MOVK (move-wide-immediate, 32-bit W-reg) and STR (immediate, unsigned offset, 32-bit).
// Pure: bytes in, instruction records out, no value tracking and no execution.
public enum Arm64Op { Movz, Movk, Str }

// For Movz/Movk: Rd = dest reg, Imm = imm16, Rn = hw shift index (shift = Rn*16).
// For Str: Rd = Rt (source reg stored), Rn = base reg, Imm = byte offset.
public readonly record struct Arm64Insn(ulong Address, Arm64Op Op, int Rd, int Rn, long Imm);

public static class Arm64Decoder
{
    public static IReadOnlyList<Arm64Insn> Decode(ReadOnlySpan<byte> text, ulong vmAddr)
    {
        var outp = new List<Arm64Insn>(text.Length / 8);
        for (int i = 0; i + 4 <= text.Length; i += 4)
        {
            uint w = (uint)(text[i] | (text[i + 1] << 8) | (text[i + 2] << 16) | (text[i + 3] << 24));
            ulong addr = vmAddr + (ulong)i;

            if ((w & 0x7F800000) == 0x52800000) // MOVZ (32-bit)
            {
                outp.Add(new Arm64Insn(addr, Arm64Op.Movz, (int)(w & 0x1F), (int)((w >> 21) & 0x3), (w >> 5) & 0xFFFF));
            }
            else if ((w & 0x7F800000) == 0x72800000) // MOVK (32-bit)
            {
                outp.Add(new Arm64Insn(addr, Arm64Op.Movk, (int)(w & 0x1F), (int)((w >> 21) & 0x3), (w >> 5) & 0xFFFF));
            }
            else if ((w & 0xFFC00000) == 0xB9000000) // STR (imm, unsigned offset, 32-bit)
            {
                long byteOff = ((w >> 10) & 0xFFF) * 4L;
                outp.Add(new Arm64Insn(addr, Arm64Op.Str, (int)(w & 0x1F), (int)((w >> 5) & 0x1F), byteOff));
            }
            // else: not one of ours, skip 4 bytes.
        }
        return outp;
    }
}
