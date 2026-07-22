namespace EggIncognito.Tests.ProtoExtract;


public static class SyntheticMacho {
    public const ulong TextVm = 0x100004000;

    public readonly record struct Sym(string Name, ulong Value);

    public static byte[] Build(byte[] text, IEnumerable<Sym> syms) {
        var symList = syms.ToList();

        var strtab = new List<byte> { 0 };
        var strx = new Dictionary<string, uint>();
        foreach (var s in symList) {
            if (strx.ContainsKey(s.Name)) continue;
            strx[s.Name] = (uint)strtab.Count;
            strtab.AddRange(System.Text.Encoding.UTF8.GetBytes(s.Name));
            strtab.Add(0);
        }

        const int headerSize = 32;
        const int segCmdSize = 72 + 80;
        const int symtabCmdSize = 24;
        int loadCmdsSize = segCmdSize + symtabCmdSize;
        int textFileOff = headerSize + loadCmdsSize;
        textFileOff = (textFileOff + 15) & ~15;

        int symoff = textFileOff + text.Length;
        symoff = (symoff + 7) & ~7;
        int nsyms = symList.Count;
        int stroff = symoff + nsyms * 16;
        int strsize = strtab.Count;
        int total = stroff + strsize;

        var bin = new byte[total];

        WU32(bin, 0, 0xFEEDFACF);
        WU32(bin, 4, 0x0100000C);
        WU32(bin, 8, 0);
        WU32(bin, 12, 2);
        WU32(bin, 16, 2);
        WU32(bin, 20, (uint)loadCmdsSize);
        WU32(bin, 24, 0);
        WU32(bin, 28, 0);

        int lc = headerSize;
        WU32(bin, lc, 0x19);
        WU32(bin, lc + 4, (uint)segCmdSize);
        WStr16(bin, lc + 8, "__TEXT");
        WU64(bin, lc + 24, TextVm);
        WU64(bin, lc + 32, (ulong)text.Length);
        WU64(bin, lc + 40, (ulong)textFileOff);
        WU64(bin, lc + 48, (ulong)text.Length);
        WU32(bin, lc + 56, 7);
        WU32(bin, lc + 60, 5);
        WU32(bin, lc + 64, 1);
        WU32(bin, lc + 68, 0);

        int sec = lc + 72;
        WStr16(bin, sec, "__text");
        WStr16(bin, sec + 16, "__TEXT");
        WU64(bin, sec + 32, TextVm);
        WU64(bin, sec + 40, (ulong)text.Length);
        WU32(bin, sec + 48, (uint)textFileOff);
        WU32(bin, sec + 52, 4);

        lc = headerSize + segCmdSize;
        WU32(bin, lc, 0x02);
        WU32(bin, lc + 4, (uint)symtabCmdSize);
        WU32(bin, lc + 8, (uint)symoff);
        WU32(bin, lc + 12, (uint)nsyms);
        WU32(bin, lc + 16, (uint)stroff);
        WU32(bin, lc + 20, (uint)strsize);

        Array.Copy(text, 0, bin, textFileOff, text.Length);

        for (int i = 0; i < nsyms; i++) {
            int e = symoff + i * 16;
            WU32(bin, e, strx[symList[i].Name]);
            bin[e + 4] = 0x0E;
            bin[e + 5] = 1;
            WU16(bin, e + 6, 0);
            WU64(bin, e + 8, symList[i].Value);
        }

        for (int i = 0; i < strtab.Count; i++) bin[stroff + i] = strtab[i];

        return bin;
    }

    private static void WU16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    private static void WU32(byte[] b, int o, uint v) {
        b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24);
    }
    private static void WU64(byte[] b, int o, ulong v) { for (int k = 0; k < 8; k++) b[o + k] = (byte)(v >> (k * 8)); }
    private static void WStr16(byte[] b, int o, string s) {
        var bytes = System.Text.Encoding.ASCII.GetBytes(s);
        Array.Copy(bytes, 0, b, o, Math.Min(bytes.Length, 16));
    }
}
