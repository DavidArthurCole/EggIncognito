using System.Text.Json.Nodes;

namespace EggIncognito.Services.ProtoExtract.Decomp;


public static class HatcheryAssemblyRecovery
{
    public readonly record struct Mat4(bool Ok, string Lambda, ExprNode?[] Cells, int OpaqueCount, string Diagnostics)
    {
       
        public float[]? Translation()
        {
            if (Cells.Length < 15 || Cells[12] is null || Cells[13] is null || Cells[14] is null) return null;
            float? V(ExprNode? n) => n is not null && ExprNode.IsFullyResolved(n) ? (float)ExprNode.Eval(n, Empty) : null;
            var x = V(Cells[12]); var y = V(Cells[13]); var z = V(Cells[14]);
            return x is { } xx && y is { } yy && z is { } zz ? [xx, yy, zz] : null;
        }

        public JsonObject ToJson() => new()
        {
            ["ok"] = Ok,
            ["lambda"] = Lambda,
            ["translation"] = Translation() is { } t ? new JsonArray(t[0], t[1], t[2]) : null,
            ["cells"] = new JsonArray(Cells.Select(c => c is null ? null : ExprNode.ToJson(c)).ToArray()),
            ["opaqueCount"] = OpaqueCount,
            ["diagnostics"] = Diagnostics,
        };
    }

   
   
    public readonly record struct Timing(float? WaitFor, bool WaitForRandom, float? SmoothDuration, int OrbitSegments, string Diagnostics)
    {
        public JsonObject ToJson() => new()
        {
            ["waitFor"] = WaitFor,
            ["waitForRandom"] = WaitForRandom,
            ["smoothDuration"] = SmoothDuration,
            ["orbitSegments"] = OrbitSegments,
            ["diagnostics"] = Diagnostics,
        };
    }

    public readonly record struct Assembly(bool Ok, float[]? Anchor, IReadOnlyList<Mat4> Transforms, Timing Timing, string Diagnostics)
    {
        public JsonObject ToJson() => new()
        {
            ["ok"] = Ok,
            ["anchor"] = Anchor is { } a ? new JsonArray(a[0], a[1], a[2]) : null,
            ["transforms"] = new JsonArray(Transforms.Select(t => (JsonNode)t.ToJson()).ToArray()),
            ["timing"] = Timing.ToJson(),
            ["diagnostics"] = Diagnostics,
        };
    }

    private static readonly IReadOnlyDictionary<string, double> Empty = new Dictionary<string, double>();

   
    private static string MatrixLambdaNeedle(string tag) =>
        $"updateHatcheryEP14GameControllerbE3{tag}FN5Eigen6MatrixIfLi4ELi4ELi0ELi4ELi4EEEvEEclEv";

    public static Assembly Recover(byte[] bin)
    {
        if (bin is null || bin.Length < 64) return new(false, null, [], default, "binary too short");
        if (!MachoText.TryFindText(bin, out var tfo, out _, out var tvm))
            return new(false, null, [], default, "no __text");
        var syms = MachoSymbols.Read(bin);

        var anchor = RecoverAnchor(bin, syms, tvm, tfo);

        var transforms = new[] { "$_2", "$_3", "$_5" }
            .Select(tag => RecoverMatrix(bin, syms, tvm, tfo, tag))
            .ToList();

        var timing = RecoverTiming(bin, syms, tvm, tfo);

        var ok = anchor is not null || transforms.Any(t => t.Ok);
        return new(ok, anchor, transforms, timing, ok ? "ok" : "nothing recovered");
    }

   
   
    private static float[]? RecoverAnchor(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, ulong tvm, int tfo)
    {
        var ex = FunctionConstantExtractor.ExtractWith(bin, syms, ["FarmScene14updateHatcheryEP14GameControllerb"]);
        if (!ex.Ok) return null;
        var f = ex.Floats;
        for (int i = 0; i + 2 < f.Count; i++)
        {
            double x = f[i], y = f[i + 1], z = f[i + 2];
            if (x is > 4 and < 40 && Math.Abs(y) < 20 && Math.Abs(z) < 20)
                return [(float)x, (float)y, (float)z];
        }
        return null;
    }

   
   
    private static Timing RecoverTiming(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, ulong tvm, int tfo)
    {
        if (!MachoSymbols.TryFindFunc(syms, ["FarmScene14rotate_pyramidEP14GameControlleri"], out var fn))
            return new(null, false, null, 0, "rotate_pyramid symbol not found");
        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, tvm, tfo, out var code, out _))
            return new(null, false, null, 0, "rotate_pyramid range out of bounds");

        var exec = Arm64SymbolicExecutor.Run(
            code, fn, tvm, tfo, syms, new Dictionary<string, ExprNode>(), KnownCallModels.Resolve);

        float? ArgOf(string method, int idx)
        {
            foreach (var c in exec.Calls)
            {
                if (!c.Name.Contains(method, StringComparison.Ordinal)) continue;
                if (idx >= c.FloatArgs.Count) continue;
                var a = c.FloatArgs[idx];
                if (ExprNode.IsFullyResolved(a)) return (float)ExprNode.Eval(a, Empty);
            }
            return null;
        }

        var waitFor = ArgOf("waitFor", 0);
        var smooth = ArgOf("smooth", 0);
        var segments = RotateSegmentCount(bin, syms);

       
       
        bool waitForRandom = waitFor is null && CalledBefore(exec, "frandom", "waitFor");

        var ok = waitFor is not null || waitForRandom || smooth is not null || segments > 0;
        return new(waitFor, waitForRandom, smooth, segments, ok ? "ok" : "no tween args resolved");
    }

   
    private static bool CalledBefore(Arm64SymbolicExecutor.ExecResult exec, string first, string then)
    {
        int firstIdx = -1;
        for (int i = 0; i < exec.Calls.Count; i++)
        {
            var name = exec.Calls[i].Name;
            if (firstIdx < 0 && name.Contains(first, StringComparison.Ordinal)) firstIdx = i;
            if (name.Contains(then, StringComparison.Ordinal)) return firstIdx >= 0 && firstIdx < i;
        }
        return false;
    }

   
   
    private static int RotateSegmentCount(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms)
    {
        var ex = FunctionConstantExtractor.ExtractWith(bin, syms, ["FarmScene14rotate_pyramidEP14GameControlleri"]);
        if (!ex.Ok) return 0;
        foreach (var f in ex.Floats)
            if (f is >= 2 and <= 16 && Math.Abs(f - Math.Round(f)) < 0.001 && Math.Abs(f - 3.14159) > 0.1)
                return (int)Math.Round(f);
        return 0;
    }

    private static Mat4 RecoverMatrix(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, ulong tvm, int tfo, string tag)
    {
        var needle = MatrixLambdaNeedle(tag);
        if (!MachoSymbols.TryFindFunc(syms, [needle], out var fn))
            return new(false, tag, new ExprNode?[16], 0, $"symbol not found: {tag}");
        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, tvm, tfo, out var code, out _))
            return new(false, tag, new ExprNode?[16], 0, "function range out of bounds");

       
        var bases = new Dictionary<string, string> { ["x0"] = "self", ["x8"] = "ret" };
        var exec = Arm64SymbolicExecutor.Run(code, fn, tvm, tfo, syms, new Dictionary<string, ExprNode>(), bases, KnownCallModels.Resolve);

        var cells = new ExprNode?[16];
        for (int i = 0; i < 16; i++)
            cells[i] = exec.RetVec.TryGetValue(i * 4, out var c) ? ExprNode.Fold(c) : null;
        var any = cells.Any(c => c is not null);
        return new(any, tag, cells, exec.Opaque, any ? "ok" : "out-param matrix not captured");
    }
}
