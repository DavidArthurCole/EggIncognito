namespace EggIncognito.Services.ProtoExtract;


public static class FunctionConstantExtractor
{
    public readonly record struct ExtractResult(bool Ok, string FunctionName, IReadOnlyList<double> Floats,
        IReadOnlyList<string> Calls, string Diagnostics);

    public static ExtractResult Extract(byte[] bin, string[] nameNeedles)
    {
        if (bin is null || bin.Length < 64) return new(false, "", [], [], "binary too short");
        return ExtractWith(bin, MachoSymbols.Read(bin), nameNeedles);
    }

   
   
   
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
