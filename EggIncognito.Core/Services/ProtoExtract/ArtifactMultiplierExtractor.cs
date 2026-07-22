namespace EggIncognito.Services.ProtoExtract;

public static class ArtifactMultiplierExtractor {
    public static readonly double[] TierMultipliers =
        [1.05, 1.08, 1.10, 1.12, 1.13, 1.15, 1.16, 1.17, 1.19, 1.20, 1.22, 1.23, 1.25, 1.28, 1.30];

    public readonly record struct ConstHit(double Value, ulong Va, int FileOff, string Section);
    public readonly record struct Result(
        bool AllAttributed,
        IReadOnlyList<ConstHit> Located,
        IReadOnlyList<double> MissingAsF64,
        string Diagnostics);

    public static Result Locate(byte[] bin) => LocateWith(bin, MachoSections.Read(bin));

    public static Result LocateWith(byte[] bin, IReadOnlyList<MachoSections.Section> sections) {
        if (bin is null || bin.Length < 64) return new(false, [], TierMultipliers, "binary too short");

        var located = new List<ConstHit>();
        var missing = new List<double>();

        foreach (var t in TierMultipliers) {
            var pat = BitConverter.GetBytes(t);
            var before = located.Count;
            for (var i = 0; i <= bin.Length - 8; i++) {
                var match = true;
                for (var k = 0; k < 8; k++)
                    if (bin[i + k] != pat[k]) { match = false; break; }
                if (!match) continue;
                if (!TryFileOffToVa(sections, i, out var va, out var owner)) continue;
                if (owner.Name != "__const") continue;
                located.Add(new ConstHit(t, va, i, $"{owner.Segment},{owner.Name}"));
            }
            if (located.Count == before) missing.Add(t);
        }

        var diag = missing.Count == 0
            ? $"{located.Count} __const hits, all {TierMultipliers.Length} multipliers present as f64"
            : $"{located.Count} __const hits; {missing.Count} absent as f64 (likely f32/derived): "
              + string.Join(",", missing.Select(m => m.ToString("0.##")));

        return new(false, located, missing, diag);
    }

    private static bool TryFileOffToVa(IReadOnlyList<MachoSections.Section> sections, int fileOff, out ulong va, out MachoSections.Section owner) {
        foreach (var s in sections) {
            if (s.VmSize == 0) continue;
            if (fileOff >= s.FileOff && fileOff < s.FileOff + (long)s.VmSize) {
                va = s.VmAddr + (ulong)(fileOff - s.FileOff);
                owner = s;
                return true;
            }
        }
        va = 0;
        owner = default;
        return false;
    }
}
