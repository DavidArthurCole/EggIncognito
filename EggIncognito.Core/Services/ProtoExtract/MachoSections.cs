namespace EggIncognito.Services.ProtoExtract;

public static class MachoSections
{
    private const uint MhMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagicLe = 0xBEBAFECA;
    private const uint CpuArm64 = 0x0100000C;
    private const uint LcSegment64 = 0x19;

    public readonly record struct Section(string Segment, string Name, ulong VmAddr, ulong VmSize, int FileOff, int MachoBase);

    public static IReadOnlyList<Section> Read(byte[] bin)
    {
        var result = new List<Section>();
        if (bin is null || bin.Length < 32) return result;
        try
        {
            uint magic = U32(bin, 0);
            int machoBase = 0;
            if (magic == FatMagic || magic == FatMagicLe)
            {
                if (!TryFindFatArm64Slice(bin, out machoBase)) return result;
                if (machoBase + 32 > bin.Length) return result;
                magic = U32(bin, machoBase);
            }
            if (magic != MhMagic64) return result;

            uint ncmds = U32(bin, machoBase + 16);
            int lc = machoBase + 32;
            for (uint c = 0; c < ncmds; c++)
            {
                if (lc + 8 > bin.Length) return result;
                uint cmd = U32(bin, lc);
                uint cmdsize = U32(bin, lc + 4);
                if (cmdsize < 8 || lc + (long)cmdsize > bin.Length) return result;
                if (cmd == LcSegment64)
                {
                    string seg = Cstr16(bin, lc + 8);
                    uint nsects = U32(bin, lc + 64);
                    int sec = lc + 72;
                    for (uint s = 0; s < nsects; s++)
                    {
                        if (sec + 80 > bin.Length) return result;
                        string sn = Cstr16(bin, sec);
                        ulong vmAddr = U64(bin, sec + 32);
                        ulong vmSize = U64(bin, sec + 40);
                        int fileOff = (int)U32(bin, sec + 48) + machoBase;
                        result.Add(new Section(seg, sn, vmAddr, vmSize, fileOff, machoBase));
                        sec += 80;
                    }
                }
                lc += (int)cmdsize;
            }
        }
        catch { return result; }
        return result;
    }

    public static bool TryVaToFileOffset(IReadOnlyList<Section> sections, ulong va, out int fileOff, out Section owner)
    {
        foreach (var s in sections)
        {
            if (s.VmSize == 0) continue;
            if (va >= s.VmAddr && va < s.VmAddr + s.VmSize)
            {
                long off = s.FileOff + (long)(va - s.VmAddr);
                if (off >= 0 && off <= int.MaxValue)
                {
                    fileOff = (int)off;
                    owner = s;
                    return true;
                }
            }
        }
        fileOff = 0;
        owner = default;
        return false;
    }

    public static Section? Find(IReadOnlyList<Section> sections, string segment, string name)
    {
        foreach (var s in sections)
            if (s.Segment == segment && s.Name == name) return s;
        return null;
    }

    private static bool TryFindFatArm64Slice(byte[] b, out int offset)
    {
        offset = 0;
        if (b.Length < 8) return false;
        uint nfat = U32be(b, 4);
        int e = 8;
        for (uint i = 0; i < nfat; i++)
        {
            if (e + 20 > b.Length) return false;
            uint cputype = U32be(b, e);
            uint off = U32be(b, e + 8);
            if (cputype == CpuArm64) { offset = (int)off; return true; }
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

    private static string Cstr16(byte[] b, int o)
    {
        int end = o;
        int max = Math.Min(o + 16, b.Length);
        while (end < max && b[end] != 0) end++;
        return System.Text.Encoding.ASCII.GetString(b, o, end - o);
    }
}
