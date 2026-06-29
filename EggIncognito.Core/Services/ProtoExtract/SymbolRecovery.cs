namespace EggIncognito.Services.ProtoExtract;

// Recovers a symbol -> target-VA map onto a STRIPPED egginc Mach-O using a SYMBOLIZED binary as reference, so
// the decomp extractor can resolve functions when no symbolized binary of the device's exact version exists.
// Two tiers, both byte-verified (never fabricates a mapping):
//   Tier 0 exact-transplant: if the two __text sections are byte-equal (same build), the reference symbol table
//     applies to the target as-is. 100% coverage. This is the real payoff when a same-version symbolized twin
//     of the device binary becomes available.
//   Tier 1 content-hash: for an adjacent version, recover the functions that are byte-identical after masking
//     pc-relative displacements (relocated-but-unchanged code). Anchors the target scan on its LC_FUNCTION_STARTS
//     (survives stripping). Measured 1.35.6 -> 1.35.8: ~27k functions recovered including the real
//     GalaxyParticle::update; functions whose body actually changed (updateSilo's main body) are not matched and
//     land in RequestedMissing. Honest: every match is byte-verified, the report names found vs missing needles.
// Pure + deterministic, no I/O. See spec docs/superpowers/specs/2026-06-29-symbol-recovery-v2-design.md.
public static class SymbolRecovery
{
    public readonly record struct RecoveryReport(
        string Tier,
        int Recovered,
        IReadOnlyList<MachoSymbols.Symbol> Symbols,
        IReadOnlyList<string> RequestedFound,
        IReadOnlyList<string> RequestedMissing,
        string Diagnostics);

    public static RecoveryReport Recover(byte[] symbolizedRef, byte[] strippedTarget, string[] interestNeedles)
    {
        interestNeedles ??= [];
        if (symbolizedRef is null || strippedTarget is null || symbolizedRef.Length < 64 || strippedTarget.Length < 64)
            return None(interestNeedles, "ref or target binary too short");

        if (!MachoText.TryFindText(symbolizedRef, out var rOff, out var rSize, out var rVm))
            return None(interestNeedles, "reference has no __text");
        if (!MachoText.TryFindText(strippedTarget, out var tOff, out var tSize, out var tVm))
            return None(interestNeedles, "target has no __text");

        var refSyms = MachoSymbols.Read(symbolizedRef);
        if (refSyms.Count == 0) return None(interestNeedles, "reference has no symbols");

        // Tier 0: byte-equal __text -> transplant the whole reference table.
        if (rSize == tSize && rSize > 0 && SpanEqual(symbolizedRef, rOff, strippedTarget, tOff, rSize))
        {
            var (found0, missing0) = Partition(refSyms, interestNeedles);
            return new RecoveryReport("exact-transplant", refSyms.Count, refSyms, found0, missing0, "ok: identical __text");
        }

        // Tier 1: content-hash recovery of byte-identical (displacement-masked) functions.
        var recovered = ContentHashRecover(symbolizedRef, rOff, rVm, refSyms, strippedTarget, tOff, tSize, tVm);
        var (found, missing) = Partition(recovered, interestNeedles);
        var diag = recovered.Count == 0
            ? "no functions recovered; target is a different build"
            : $"content-hash recovered {recovered.Count} of {CountTextFuncs(refSyms, rVm, rSize)} reference functions";
        return new RecoveryReport("content-hash", recovered.Count, recovered, found, missing, diag);
    }

    // 8 instructions; skips tiny shared stubs + thunks that pile into hot prefix buckets without being useful.
    private const int MinFuncLen = 32;
    private const int MaxFuncLen = 0x20000;
    // normalized 32-byte prefix = the cheap candidate filter; longer than 16 to break up hot prologue buckets.
    private const int PrefixLen = 32;

    // Recover function symbols whose normalized body is byte-identical between ref and target. A naive scan of
    // every target offset at every reference length is O(textSize x lengths) and takes ~100s on a 34MB text.
    // Instead: index reference functions by a cheap normalized 32-byte-prefix hash, scan the target only at its
    // real function starts, and run the expensive byte-exact compare ONLY on prefix-bucket hits. ~1-2s.
    private static List<MachoSymbols.Symbol> ContentHashRecover(
        byte[] refBin, int rOff, ulong rVm, IReadOnlyList<MachoSymbols.Symbol> refSyms,
        byte[] tgtBin, int tOff, int tSize, ulong tVm)
    {
        var rSlide = rVm - (ulong)rOff;
        var ranges = FunctionRanges(refSyms, rVm, rVm + (ulong)refBin.Length);

        // prefix index: cheap FNV of the normalized first 32 bytes -> list of candidate functions.
        var byPrefix = new Dictionary<ulong, List<RefFunc>>();
        foreach (var (name, start, end) in ranges)
        {
            var fileStart = (long)start - (long)rSlide;
            var len = (long)end - (long)start;
            if (len < MinFuncLen || len > MaxFuncLen || fileStart < 0 || fileStart + len > refBin.Length) continue;
            var norm = NormalizeRange(refBin, (int)fileStart, (int)len);
            var pfx = FnvPrefix(norm);
            var fn = new RefFunc(name, norm, FnvFull(norm));
            if (!byPrefix.TryGetValue(pfx, out var list)) byPrefix[pfx] = list = [];
            list.Add(fn);
        }
        if (byPrefix.Count == 0) return [];

        var tEnd = Math.Min(tOff + tSize, tgtBin.Length);
        var recovered = new List<MachoSymbols.Symbol>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedFullHashes = new HashSet<ulong>();

        // Scan only the target's real function starts (LC_FUNCTION_STARTS survives symbol stripping): far fewer
        // candidates than a dense 4-byte sweep and aligned to true boundaries, so fewer false prefix collisions.
        // Fall back to a dense sweep only if the table is absent.
        var starts = MachoFunctionStarts.Read(tgtBin);
        IEnumerable<int> Offsets()
        {
            if (starts.Count > 0)
            {
                foreach (var s in starts) if (s >= tOff && s + PrefixLen <= tEnd) yield return s;
            }
            else
            {
                for (int p = tOff; p + PrefixLen <= tEnd; p += 4) yield return p;
            }
        }

        foreach (var p in Offsets())
        {
            var pfx = FnvNormalizedPrefixAt(tgtBin, p);
            if (!byPrefix.TryGetValue(pfx, out var cands)) continue;
            foreach (var fn in cands)
            {
                if (p + fn.Norm.Length > tEnd) continue;
                if (usedNames.Contains(fn.Name) || usedFullHashes.Contains(fn.FullHash)) continue;
                if (!NormalizedEquals(tgtBin, p, fn.Norm)) continue;
                usedNames.Add(fn.Name);
                usedFullHashes.Add(fn.FullHash);
                recovered.Add(new MachoSymbols.Symbol(fn.Name, tVm + (ulong)(p - tOff), 0, 1));
                break;
            }
        }
        return recovered;
    }

    private readonly record struct RefFunc(string Name, byte[] Norm, ulong FullHash);

    // FNV-1a over a whole normalized body. Collisions are caught by the subsequent byte-exact NormalizedEquals.
    private static ulong FnvFull(byte[] norm)
    {
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < norm.Length; i++) { h ^= norm[i]; h *= 1099511628211UL; }
        return h;
    }

    // FNV-1a over the normalized first PrefixLen bytes of an already-normalized buffer.
    private static ulong FnvPrefix(byte[] norm)
    {
        ulong h = 1469598103934665603UL;
        int n = Math.Min(PrefixLen, norm.Length);
        for (int i = 0; i < n; i++) { h ^= norm[i]; h *= 1099511628211UL; }
        return h;
    }

    // FNV-1a over the normalized first PrefixLen bytes starting at off in a RAW target buffer, allocation-free:
    // each 4-byte word is normalized on the fly. This is the hot path scanned at every target function start.
    private static ulong FnvNormalizedPrefixAt(byte[] bin, int off)
    {
        ulong h = 1469598103934665603UL;
        for (int i = 0; i < PrefixLen; i += 4)
        {
            uint w = NormalizeWord((uint)(bin[off + i] | (bin[off + i + 1] << 8) | (bin[off + i + 2] << 16) | (bin[off + i + 3] << 24)));
            h ^= (byte)w; h *= 1099511628211UL;
            h ^= (byte)(w >> 8); h *= 1099511628211UL;
            h ^= (byte)(w >> 16); h *= 1099511628211UL;
            h ^= (byte)(w >> 24); h *= 1099511628211UL;
        }
        return h;
    }

    // Compare a target window at off against an already-normalized reference body, normalizing target words on
    // the fly. Allocation-free; only runs on prefix collisions.
    private static bool NormalizedEquals(byte[] tgt, int off, byte[] norm)
    {
        for (int i = 0; i + 4 <= norm.Length; i += 4)
        {
            uint w = NormalizeWord((uint)(tgt[off + i] | (tgt[off + i + 1] << 8) | (tgt[off + i + 2] << 16) | (tgt[off + i + 3] << 24)));
            if ((byte)w != norm[i] || (byte)(w >> 8) != norm[i + 1] || (byte)(w >> 16) != norm[i + 2] || (byte)(w >> 24) != norm[i + 3])
                return false;
        }
        return true;
    }

    // Function [start,end) ranges from symbol addresses: end = next symbol address, capped at textEnd.
    private static List<(string Name, ulong Start, ulong End)> FunctionRanges(
        IReadOnlyList<MachoSymbols.Symbol> syms, ulong textVm, ulong textEnd)
    {
        var addrs = syms.Where(s => s.Value >= textVm && s.Value < textEnd && !string.IsNullOrEmpty(s.Name))
            .Select(s => (s.Name, s.Value)).Distinct().OrderBy(s => s.Value).ToList();
        var outp = new List<(string, ulong, ulong)>(addrs.Count);
        for (int i = 0; i < addrs.Count; i++)
        {
            var start = addrs[i].Value;
            var end = i + 1 < addrs.Count ? addrs[i + 1].Value : textEnd;
            if (end > start) outp.Add((addrs[i].Name, start, end));
        }
        return outp;
    }

    private static int CountTextFuncs(IReadOnlyList<MachoSymbols.Symbol> syms, ulong textVm, int textSize)
        => FunctionRanges(syms, textVm, textVm + (ulong)textSize).Count;

    // Mask the immediate bytes of pc-relative instructions so a relocated-but-unchanged function still matches.
    // arm64 is fixed 4-byte. Keeps the opcode byte; zeroes the displacement-bearing low bytes.
    private static byte[] NormalizeRange(byte[] bin, int off, int len)
    {
        var c = new byte[len];
        Array.Copy(bin, off, c, 0, len);
        for (int i = 0; i + 4 <= len; i += 4)
        {
            uint w = (uint)(c[i] | (c[i + 1] << 8) | (c[i + 2] << 16) | (c[i + 3] << 24));
            uint nw = NormalizeWord(w);
            c[i] = (byte)nw; c[i + 1] = (byte)(nw >> 8); c[i + 2] = (byte)(nw >> 16); c[i + 3] = (byte)(nw >> 24);
        }
        return c;
    }

    // Zero the displacement bytes of a pc-relative instruction word, keep the opcode byte. Identity otherwise.
    private static uint NormalizeWord(uint w)
    {
        uint top6 = w >> 26;
        uint top8 = w >> 24;
        bool brImm = top6 == 0b000101 || top6 == 0b100101; // b / bl
        bool bcond = top8 == 0b01010100; // b.cond
        bool adr = (w & 0x1F000000) == 0x10000000; // adr / adrp family
        return (brImm || bcond || adr) ? (w & 0xFF000000) : w;
    }

    private static (IReadOnlyList<string> Found, IReadOnlyList<string> Missing) Partition(
        IReadOnlyList<MachoSymbols.Symbol> syms, string[] needles)
    {
        var found = new List<string>();
        var missing = new List<string>();
        foreach (var n in needles)
        {
            if (syms.Any(s => s.Name.Contains(n, StringComparison.Ordinal))) found.Add(n);
            else missing.Add(n);
        }
        return (found, missing);
    }

    private static RecoveryReport None(string[] needles, string diag)
        => new("none", 0, [], [], needles, diag);

    private static bool SpanEqual(byte[] a, int aOff, byte[] b, int bOff, int len)
    {
        if (aOff < 0 || bOff < 0 || aOff + len > a.Length || bOff + len > b.Length) return false;
        return a.AsSpan(aOff, len).SequenceEqual(b.AsSpan(bOff, len));
    }
}
