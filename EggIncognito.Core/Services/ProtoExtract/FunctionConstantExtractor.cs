namespace EggIncognito.Services.ProtoExtract;

// Reads the float/double constants + call targets out of a named function in an arm64 Mach-O. Resolves the
// function's VA range by symbol name (MachoSymbols.TryFindFunc, the proven n_value path; raw symbol addresses
// are unreliable for the C++ mangled set), locates __text for the slide, disassembles the range, and maps each
// bl target back to its nearest symbol. The reusable extraction primitive behind /api/decomp/*.
public static class FunctionConstantExtractor
{
    public readonly record struct ExtractResult(bool Ok, string FunctionName, IReadOnlyList<double> Floats,
        IReadOnlyList<string> Calls, string Diagnostics);

    public static ExtractResult Extract(byte[] bin, string[] nameNeedles)
    {
        if (bin is null || bin.Length < 64) return new(false, "", [], [], "binary too short");
        return ExtractWith(bin, MachoSymbols.Read(bin), nameNeedles);
    }

    // Extract using an EXPLICIT symbol list instead of the binary's own table. The recovery path (v2) hands a
    // stripped target binary plus the (name, target-VA) symbols recovered from a symbolized reference, so the
    // extractor can resolve functions the stripped binary lost. Pass the full recovered set so a function's end
    // VA can be inferred from the next symbol.
    public static ExtractResult ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, string[] nameNeedles)
    {
        if (bin is null || bin.Length < 64) return new(false, "", [], [], "binary too short");
        if (!MachoText.TryFindText(bin, out var textFileOff, out _, out var textVmAddr))
            return new(false, "", [], [], "no __text section");

        if (!MachoSymbols.TryFindFunc(syms, nameNeedles, out var fn))
            return new(false, "", [], [], $"symbol not found: {string.Join("|", nameNeedles)}");

        var analysis = MachoArm64Disassembler.Analyze(bin, fn.Start, fn.End, textVmAddr, textFileOff);
        var floats = analysis.Floats.Select(f => f.Value).ToList();
        var calls = analysis.CallTargets.Select(t => ResolveCallName(syms, t)).Distinct().ToList();
        return new(true, fn.Name, floats, calls, "ok");
    }

    // The name of the symbol whose address is the greatest <= target (the function the bl lands in), or the
    // hex VA when target is below every symbol.
    public static string ResolveCallName(IReadOnlyList<MachoSymbols.Symbol> syms, ulong target)
    {
        string? best = null;
        ulong bestAddr = 0;
        foreach (var s in syms)
        {
            if (s.Value == 0 || string.IsNullOrEmpty(s.Name)) continue;
            if (s.Value <= target && s.Value >= bestAddr) { bestAddr = s.Value; best = s.Name; }
        }
        return best ?? $"0x{target:x}";
    }
}
