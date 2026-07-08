namespace EggIncognito.Services.ProtoExtract;

// Parses a Mach-O LC_SYMTAB into (name, vmAddress) pairs. The iOS egginc binary is not fully stripped: its C++
// method symbols survive, letting a disassembler scope the search to one function instead of the whole
// noise-flooded .text.
public static class MachoSymbols
{
    private const uint MhMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagicLe = 0xBEBAFECA;
    private const uint CpuArm64 = 0x0100000C;
    private const uint LcSymtab = 0x02;
    private const uint LcSegment64 = 0x19;

    public readonly record struct Symbol(string Name, ulong Value, byte Type, byte Sect);

    // Address range of a __text-resident function: [Start, End) in vm address space. End is the next
    // symbol's address (functions are not sized in nlist), capped at the section end.
    public readonly record struct FuncRange(string Name, ulong Start, ulong End);

    public static IReadOnlyList<Symbol> Read(byte[] bin)
    {
        var outp = new List<Symbol>();
        if (bin is null || bin.Length < 32) return outp;
        try
        {
            uint magic = U32(bin, 0);
            int b = 0;
            if (magic == FatMagic || magic == FatMagicLe)
            {
                if (!TryFatArm64(bin, out b) || b + 32 > bin.Length) return outp;
                magic = U32(bin, b);
            }
            if (magic != MhMagic64) return outp;

            uint ncmds = U32(bin, b + 16);
            int lc = b + 32;
            for (uint c = 0; c < ncmds; c++)
            {
                if (lc + 8 > bin.Length) return outp;
                uint cmd = U32(bin, lc);
                uint cmdsize = U32(bin, lc + 4);
                if (cmdsize < 8 || lc + (long)cmdsize > bin.Length) return outp;
                if (cmd == LcSymtab)
                {
                    uint symoff = U32(bin, lc + 8) + (uint)b;
                    uint nsyms = U32(bin, lc + 12);
                    uint stroff = U32(bin, lc + 16) + (uint)b;
                    uint strsize = U32(bin, lc + 20);
                    ReadNlist(bin, symoff, nsyms, stroff, strsize, outp);
                }
                lc += (int)cmdsize;
            }
        }
        catch
        {
        }
        return outp;
    }

    // Find the address range of the first symbol whose name contains every needle (substring match on the
    // mangled name). End = the smallest symbol address strictly greater than Start (next function), so the
    // disassembler reads only that function's bytes.
    public static bool TryFindFunc(IReadOnlyList<Symbol> syms, string[] needles, out FuncRange range)
    {
        range = default;
        Symbol? hit = null;
        foreach (var s in syms)
        {
            if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
            bool all = true;
            foreach (var n in needles) if (!s.Name.Contains(n)) { all = false; break; }
            if (all) { hit = s; break; }
        }
        if (hit is null) return false;

        ulong start = hit.Value.Value;
        ulong end = ulong.MaxValue;
        foreach (var s in syms)
            if (s.Value > start && s.Value < end) end = s.Value;
        if (end == ulong.MaxValue) end = start + 0x4000; // fallback window when no later symbol
        range = new FuncRange(hit.Value.Name, start, end);
        return true;
    }

    private static void ReadNlist(byte[] bin, uint symoff, uint nsyms, uint stroff, uint strsize, List<Symbol> outp)
    {
        for (uint i = 0; i < nsyms; i++)
        {
            long e = symoff + (long)i * 16;
            if (e + 16 > bin.Length) return;
            uint nStrx = U32(bin, (int)e);
            byte nType = bin[e + 4];
            byte nSect = bin[e + 5];
            ulong nValue = U64(bin, (int)e + 8);
            if (nStrx == 0 || nStrx >= strsize) continue;
            string name = Cstr(bin, (int)(stroff + nStrx));
            if (name.Length == 0) continue;
            outp.Add(new Symbol(name, nValue, nType, nSect));
        }
    }

    private static bool TryFatArm64(byte[] b, out int offset)
    {
        offset = 0;
        if (b.Length < 8) return false;
        uint nfat = U32be(b, 4);
        int e = 8;
        for (uint i = 0; i < nfat; i++)
        {
            if (e + 20 > b.Length) return false;
            if (U32be(b, e) == CpuArm64) { offset = (int)U32be(b, e + 8); return true; }
            e += 20;
        }
        return false;
    }

    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static uint U32be(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);

    private static ulong U64(byte[] b, int o)
    {
        ulong v = 0;
        for (int k = 0; k < 8; k++) v |= (ulong)b[o + k] << (k * 8);
        return v;
    }

    private static string Cstr(byte[] b, int o)
    {
        if (o < 0 || o >= b.Length) return "";
        int end = o;
        while (end < b.Length && b[end] != 0) end++;
        return System.Text.Encoding.UTF8.GetString(b, o, end - o);
    }
}
