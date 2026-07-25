namespace EggIncognito.Services.ProtoExtract;

public static class Arm64ClientVersionScanner {
    private const uint MovzMask = 0x7FA00000, MovzMatch = 0x52800000;

    private const uint StrMask = 0xFFC00000, StrMatch = 0xB9000000;

    public static ScanResult Scan(byte[] textBytes, int prevClientVersion) {
        int n = textBytes.Length / 4;
        uint[] words = new uint[n];
        for (int i = 0; i < n; i++) {
            words[i] = (uint)(textBytes[i * 4] | (textBytes[i * 4 + 1] << 8)
                                               | (textBytes[i * 4 + 2] << 16) | (textBytes[i * 4 + 3] << 24));
        }

        var pair = new Dictionary<(int Off, int Val), HashSet<int>>();
        for (int i = 0; i < n; i++) {
            uint w = words[i];
            if ((w & StrMask) != StrMatch) continue;
            int rt = (int)(w & 0x1F);
            int off = (int)(((w >> 10) & 0xFFF) * 4);
            int? val = PrevMovzImm(words, i, rt);
            if (val is null or < 2 or > 255) continue;
            if (!pair.TryGetValue((off, val.Value), out var set)) pair[(off, val.Value)] = set = [];
            set.Add(i);
        }


        var counts = new Dictionary<int, int>();
        foreach (((int _, int val), var sites) in pair) {
            if (sites.Count >= 3)
                counts[val] = Math.Max(counts.GetValueOrDefault(val, 0), sites.Count);
        }

        int? chosen = Pick(counts, prevClientVersion);
        var cands = counts.Keys.ToList();
        cands.Sort();
        return new ScanResult(chosen, cands);
    }


    private static int? PrevMovzImm(uint[] words, int i, int rt) {
        for (int j = i - 1; j >= Math.Max(i - 4, 0); j--) {
            uint w = words[j];
            if ((w & MovzMask) != MovzMatch) continue;
            if ((int)(w & 0x1F) != rt) continue;
            return (int)((w >> 5) & 0xFFFF);
        }

        return null;
    }


    private static int? Pick(Dictionary<int, int> cands, int prev) {
        int? best = null;
        foreach (int v in cands.Keys) {
            if (v < prev || v > prev + 2) continue;
            if (best is null) {
                best = v;
                continue;
            }

            int da = Math.Abs(v - prev), db = Math.Abs(best.Value - prev);
            if (da < db || (da == db && cands[v] > cands[best.Value])) best = v;
        }

        return best;
    }

    public sealed record ScanResult(int? ClientVersion, IReadOnlyList<int> Candidates);
}
