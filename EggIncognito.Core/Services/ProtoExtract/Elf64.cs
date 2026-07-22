using System.Text;

namespace EggIncognito.Services.ProtoExtract;

public static class Elf64 {
    public sealed record Section(ulong VAddr, long FileOffset, long Size);

    public static Section? FindSection(byte[] elf, string name) {
        try {
            if (elf is null || elf.Length < 64) return null;
            if (elf[0] != 0x7F || elf[1] != (byte)'E' || elf[2] != (byte)'L' || elf[3] != (byte)'F') return null;
            if (elf[4] != 2 || elf[5] != 1) return null;

            ulong shoff = U64(elf, 0x28);
            int shentsize = U16(elf, 0x3A);
            int shnum = U16(elf, 0x3C);
            int shstrndx = U16(elf, 0x3E);
            if (shentsize < 64 || shnum <= 0 || shstrndx >= shnum) return null;

            long ShAt(int i) => (long)shoff + (long)i * shentsize;
            long strHdr = ShAt(shstrndx);
            if (strHdr + 64 > elf.Length) return null;
            long strOff = (long)U64(elf, (int)strHdr + 0x18);

            for (int i = 0; i < shnum; i++) {
                long h = ShAt(i);
                if (h + 64 > elf.Length) break;
                uint nameOff = U32(elf, (int)h + 0x00);
                if (SectionName(elf, strOff, nameOff) != name) continue;
                return new Section(U64(elf, (int)h + 0x10), (long)U64(elf, (int)h + 0x18), (long)U64(elf, (int)h + 0x20));
            }
            return null;
        } catch {
            return null;
        }
    }

    private static string SectionName(byte[] elf, long strOff, uint nameOff) {
        long start = strOff + nameOff;
        if (start < 0 || start >= elf.Length) return "";
        long end = start;
        while (end < elf.Length && elf[end] != 0) end++;
        return Encoding.ASCII.GetString(elf, (int)start, (int)(end - start));
    }

    private static ushort U16(byte[] b, int p) => (ushort)(b[p] | (b[p + 1] << 8));
    private static uint U32(byte[] b, int p) => (uint)(b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24));
    private static ulong U64(byte[] b, int p) {
        ulong v = 0;
        for (int i = 0; i < 8; i++) v |= (ulong)b[p + i] << (8 * i);
        return v;
    }
}
