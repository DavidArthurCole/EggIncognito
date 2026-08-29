namespace EggIncognito.Core.Services.ProtoExtract;

public enum Arm64Op {
    Movz,
    Movk,
    Str
}

public readonly record struct Arm64Insn(ulong Address, Arm64Op Op, int Rd, int Rn, long Imm);

public static class Arm64Decoder {
    public static IReadOnlyList<Arm64Insn> Decode(ReadOnlySpan<byte> text, ulong vmAddr) {
        var outp = new List<Arm64Insn>(text.Length / 8);
        for (int i = 0; i + 4 <= text.Length; i += 4) {
            uint w = (uint)(text[i] | (text[i + 1] << 8) | (text[i + 2] << 16) | (text[i + 3] << 24));
            ulong addr = vmAddr + (ulong)i;

            if ((w & 0x7F800000) == 0x52800000) {
                outp.Add(new Arm64Insn(addr, Arm64Op.Movz, (int)(w & 0x1F), (int)((w >> 21) & 0x3), (w >> 5) & 0xFFFF));
            } else if ((w & 0x7F800000) == 0x72800000) {
                outp.Add(new Arm64Insn(addr, Arm64Op.Movk, (int)(w & 0x1F), (int)((w >> 21) & 0x3), (w >> 5) & 0xFFFF));
            } else if ((w & 0xFFC00000) == 0xB9000000) {
                long byteOff = ((w >> 10) & 0xFFF) * 4L;
                outp.Add(new Arm64Insn(addr, Arm64Op.Str, (int)(w & 0x1F), (int)((w >> 5) & 0x1F), byteOff));
            }
        }

        return outp;
    }
}
