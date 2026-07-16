namespace EggIncognito.Services.ProtoExtract;

public static class MachoText
{
    private const uint MhMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagicLe = 0xBEBAFECA;
    private const uint CpuArm64 = 0x0100000C;
    private const uint LcSegment64 = 0x19;

    public static bool TryFindText(byte[] bin, out int fileOff, out int size, out ulong vmAddr)
    {
        fileOff = 0; size = 0; vmAddr = 0;
        if (bin is null || bin.Length < 32) return false;
        try
        {
            uint magic = U32(bin, 0);
            int machoBase = 0;
            if (magic == FatMagic || magic == FatMagicLe)
            {
                if (!TryFindFatArm64Slice(bin, out machoBase)) return false;
                if (machoBase + 32 > bin.Length) return false;
                magic = U32(bin, machoBase);
            }
            if (magic != MhMagic64) return false;

            uint ncmds = U32(bin, machoBase + 16);
            int lc = machoBase + 32;
            for (uint c = 0; c < ncmds; c++)
            {
                if (lc + 8 > bin.Length) return false;
                uint cmd = U32(bin, lc);
                uint cmdsize = U32(bin, lc + 4);
                if (cmdsize < 8 || lc + (long)cmdsize > bin.Length) return false;
                if (cmd == LcSegment64)
                {
                    string seg = Cstr16(bin, lc + 8);
                    if (seg == "__TEXT")
                    {
                        uint nsects = U32(bin, lc + 64);
                        int sec = lc + 72;
                        for (uint s = 0; s < nsects; s++)
                        {
                            if (sec + 80 > bin.Length) return false;
                            string sn = Cstr16(bin, sec);
                            if (sn == "__text")
                            {
                                vmAddr = U64(bin, sec + 32);
                                size = (int)U64(bin, sec + 40);
                                fileOff = (int)U32(bin, sec + 48) + machoBase;
                                if (fileOff < 0 || size < 0 || (long)fileOff + size > bin.Length) return false;
                                return size > 0;
                            }
                            sec += 80;
                        }
                    }
                }
                lc += (int)cmdsize;
            }
            return false;
        }
        catch
        {
            return false;
        }
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
