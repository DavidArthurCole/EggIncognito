using System.Text.RegularExpressions;
using EggIncognito.Services.ProtoExtract;
using Xunit.Abstractions;

namespace EggIncognito.Tests;

public partial class MachoClientVersionSurveyTests(ITestOutputHelper output) {
    private static readonly string Root =
        Environment.GetEnvironmentVariable("EGGINC_IOS_HISTORICAL_ROOT") ?? @"C:\Users\david\egginc-ios-extract\historical";

    [Fact]
    public void Survey_AllHistoricalBinaries() {
        if (!Directory.Exists(Root)) {
            output.WriteLine($"SKIP: {Root} not present");
            return;
        }

        foreach (var appDir in Directory.GetDirectories(Root)) {
            string? bin = FindAppBinary(appDir);
            if (bin is null) continue;

            string label = Path.GetFileName(appDir);
            string ver = ReadShortVersion(Path.Combine(Path.GetDirectoryName(bin)!, "Info.plist")) ?? "?";

            byte[] bytes;
            try { bytes = File.ReadAllBytes(bin); } catch { output.WriteLine($"{label}: read error"); continue; }

            if (!MachoText.TryFindText(bytes, out int off, out int size, out ulong vm)) {
                output.WriteLine($"{label} (v{ver}): no __text (not thin/FAT ARM64 Mach-O)");
                continue;
            }

            var insns = Arm64Decoder.Decode(bytes.AsSpan(off, size), vm);
            var ranked = RankBySites(insns, 2, 255);
            string band = string.Join(", ", ranked
                .Where(kv => kv.Value >= 3)
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}x{kv.Value}"));
            output.WriteLine($"{label} (v{ver}): cands>=3 = [{band}]");
        }
    }

    private static List<KeyValuePair<int, int>> RankBySites(IReadOnlyList<Arm64Insn> insns, int lo, int hi) {
        var pair = new Dictionary<(int, long, int), HashSet<ulong>>();
        for (int i = 0; i < insns.Count; i++) {
            if (insns[i].Op != Arm64Op.Str) continue;
            var str = insns[i];
            int? val = Resolve(insns, i, str.Rd);
            if (val is null || val < lo || val > hi) continue;
            var key = (str.Rn, str.Imm, val.Value);
            if (!pair.TryGetValue(key, out var set)) pair[key] = set = [];
            set.Add(str.Address);
        }
        var perValue = new Dictionary<int, int>();
        foreach (var (k, sites) in pair)
            perValue[k.Item3] = Math.Max(perValue.TryGetValue(k.Item3, out var c) ? c : 0, sites.Count);
        return [.. perValue.OrderByDescending(kv => kv.Value)];
    }

    private static int? Resolve(IReadOnlyList<Arm64Insn> insns, int strIndex, int reg) {
        int value = 0; bool seeded = false;
        for (int j = Math.Max(0, strIndex - 4); j < strIndex; j++) {
            var ins = insns[j];
            if (ins.Rd != reg) continue;
            int shift = ins.Rn * 16;
            if (ins.Op == Arm64Op.Movz) { value = (int)(ins.Imm << shift); seeded = true; } else if (ins.Op == Arm64Op.Movk && seeded) { int mask = ~(0xFFFF << shift); value = (value & mask) | (int)(ins.Imm << shift); }
        }
        return seeded ? value : null;
    }

    private static string? FindAppBinary(string appDir) {
        var payload = Path.Combine(appDir, "Payload");
        if (!Directory.Exists(payload)) return null;
        foreach (var dotApp in Directory.GetDirectories(payload, "*.app")) {
            string name = Path.GetFileNameWithoutExtension(dotApp);
            string cand = Path.Combine(dotApp, name);
            if (File.Exists(cand)) return cand;
        }
        return null;
    }

    private static string? ReadShortVersion(string plistPath) {
        if (!File.Exists(plistPath)) return null;
        try {
            string text = File.ReadAllText(plistPath);
            var m = ShortVersionRegex().Match(text);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        } catch { return null; }
    }

    [GeneratedRegex(@"CFBundleShortVersionString</key>\s*<string>([^<]+)</string>")]
    private static partial Regex ShortVersionRegex();
}
