namespace EggIncognito.Services.ProtoExtract;

// Recovers Egg Inc's hardcoded clientVersion (BasicRequestInfo.client_version) from the disassembled
// .text of libegginc.so: a compiled-in small int the request-builder functions write to a struct field,
// not present in the .proto. Heuristic: find small ints (2..255) MOVZ'd into a Wn then STR'd to the same
// struct offset from >= 3 distinct call sites, then disambiguate with the previous known clientVersion
// (it increments by 0-1, rarely 2, per build). Only two ARM64 instruction forms are decoded.
public static class Arm64ClientVersionScanner
{
    public sealed record ScanResult(int? ClientVersion, IReadOnlyList<int> Candidates);

    // MOVZ Wd, #imm16 (sf=0, no shift)
    private const uint MovzMask = 0x7FA00000, MovzMatch = 0x52800000;
    // STR Wt, [Xn, #imm12] (32-bit)
    private const uint StrMask = 0xFFC00000, StrMatch = 0xB9000000;

    public static ScanResult Scan(byte[] textBytes, int prevClientVersion)
    {
        int n = textBytes.Length / 4;
        var words = new uint[n];
        for (int i = 0; i < n; i++)
            words[i] = (uint)(textBytes[i * 4] | (textBytes[i * 4 + 1] << 8)
                            | (textBytes[i * 4 + 2] << 16) | (textBytes[i * 4 + 3] << 24));

        // (offset, value) -> distinct instruction-index set.
        var pair = new Dictionary<(int Off, int Val), HashSet<int>>();
        for (int i = 0; i < n; i++)
        {
            uint w = words[i];
            if ((w & StrMask) != StrMatch) continue;
            int rt = (int)(w & 0x1F);
            int off = (int)(((w >> 10) & 0xFFF) * 4);
            int? val = PrevMovzImm(words, i, rt);
            if (val is null || val < 2 || val > 255) continue;
            if (!pair.TryGetValue((off, val.Value), out var set)) pair[(off, val.Value)] = set = new HashSet<int>();
            set.Add(i);
        }

        // value -> max distinct-site count among offsets it is written to, for sites >= 3.
        var counts = new Dictionary<int, int>();
        foreach (var ((_, val), sites) in pair)
            if (sites.Count >= 3)
                counts[val] = Math.Max(counts.TryGetValue(val, out var c) ? c : 0, sites.Count);

        int? chosen = Pick(counts, prevClientVersion);
        var cands = counts.Keys.ToList();
        cands.Sort();
        return new ScanResult(chosen, cands);
    }

    // Looks back up to 4 instructions for a MOVZ writing Wt; returns its imm16.
    private static int? PrevMovzImm(uint[] words, int i, int rt)
    {
        for (int j = i - 1; j >= Math.Max(i - 4, 0); j--)
        {
            uint w = words[j];
            if ((w & MovzMask) != MovzMatch) continue;
            if ((int)(w & 0x1F) != rt) continue;
            return (int)((w >> 5) & 0xFFFF);
        }
        return null;
    }

    // clientVersion sits in {prev, prev+1, prev+2}; nearest to prev, tie-break by descending site-count.
    private static int? Pick(Dictionary<int, int> cands, int prev)
    {
        int? best = null;
        foreach (var v in cands.Keys)
        {
            if (v < prev || v > prev + 2) continue;
            if (best is null) { best = v; continue; }
            int da = Math.Abs(v - prev), db = Math.Abs(best.Value - prev);
            if (da < db || (da == db && cands[v] > cands[best.Value])) best = v;
        }
        return best;
    }
}
