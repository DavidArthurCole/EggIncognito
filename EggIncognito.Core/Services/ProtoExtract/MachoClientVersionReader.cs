namespace EggIncognito.Services.ProtoExtract;

// Reads the Egg Inc clientVersion (BasicRequestInfo.client_version, a compiled-in uint32) out of a decrypted
// iOS Mach-O. A small int written by MOVZ/MOVK then STR to the same (baseReg, structOffset) from >=3 distinct
// sites is a candidate; the real clientVersion is disambiguated against the previous known value, since it
// increments 0-2 per build. Null when prev is unknown or no in-range candidate exists.
public static class MachoClientVersionReader
{
    public sealed record ClientVersionResult(int? ClientVersion, IReadOnlyList<int> Candidates);

    public static ClientVersionResult Read(byte[] macho, int? previousClientVersion)
    {
        if (!MachoText.TryFindText(macho, out int off, out int size, out ulong vm))
            return new ClientVersionResult(null, []);

        var insns = Arm64Decoder.Decode(macho.AsSpan(off, size), vm);
        var cands = Candidates(insns);
        var chosen = Pick(cands, previousClientVersion);
        var sorted = cands.Keys.OrderBy(k => k).ToList();
        return new ClientVersionResult(chosen, sorted);
    }

    // value -> max distinct STR-site count among the (baseReg, offset) keys it is written to. A candidate
    // is a value in 2..255 written to the SAME key from >= 3 distinct STR addresses.
    internal static Dictionary<int, int> Candidates(IReadOnlyList<Arm64Insn> insns)
    {
        var pair = new Dictionary<(int baseReg, long off, int val), HashSet<ulong>>();
        for (int i = 0; i < insns.Count; i++)
        {
            if (insns[i].Op != Arm64Op.Str) continue;
            var str = insns[i];
            int? val = ResolveReg(insns, i, str.Rd); // Rd holds Rt (source reg) for STR
            if (val is null || val < 2 || val > 255) continue;
            var key = (str.Rn, str.Imm, val.Value);
            if (!pair.TryGetValue(key, out var set)) pair[key] = set = new HashSet<ulong>();
            set.Add(str.Address);
        }

        var outp = new Dictionary<int, int>();
        foreach (var (key, sites) in pair)
        {
            if (sites.Count < 3) continue;
            int v = key.val;
            outp[v] = Math.Max(outp.TryGetValue(v, out var c) ? c : 0, sites.Count);
        }
        return outp;
    }

    // Looks back up to 4 instructions for MOVZ/MOVK targeting reg and resolves the 32-bit constant. MOVK
    // overlays the 16-bit lane at (hw*16) onto the value seeded by the nearest preceding MOVZ.
    private static int? ResolveReg(IReadOnlyList<Arm64Insn> insns, int strIndex, int reg)
    {
        int value = 0;
        bool seeded = false;
        int start = Math.Max(0, strIndex - 4);
        for (int j = start; j < strIndex; j++)
        {
            var ins = insns[j];
            if (ins.Rd != reg) continue;
            int shift = ins.Rn * 16; // hw
            if (ins.Op == Arm64Op.Movz)
            {
                value = (int)(ins.Imm << shift);
                seeded = true;
            }
            else if (ins.Op == Arm64Op.Movk && seeded)
            {
                int mask = ~(0xFFFF << shift);
                value = (value & mask) | (int)(ins.Imm << shift);
            }
        }
        return seeded ? value : null;
    }

    // clientVersion increments by 0-2 per build, so it sits in {prev, prev+1, prev+2}. Among in-range
    // candidates, nearest to prev, tie-broken by descending site count. Null when prev null or none in range.
    public static int? Pick(IReadOnlyDictionary<int, int> cands, int? prev)
    {
        if (prev is null) return null;
        int p = prev.Value;
        var inRange = cands.Keys.Where(v => v >= p && v <= p + 2).ToList();
        if (inRange.Count == 0) return null;
        return inRange
            .OrderBy(v => Math.Abs(v - p))
            .ThenByDescending(v => cands[v])
            .First();
    }
}
