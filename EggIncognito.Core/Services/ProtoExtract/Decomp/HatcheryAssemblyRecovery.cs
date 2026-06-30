using System.Text.Json.Nodes;

namespace EggIncognito.Services.ProtoExtract.Decomp;

// Recovers the hatchery floating-piece assembly from FarmScene::updateHatchery: the body/ball ANCHOR + each
// matrix-lambda's returned 4x4 transform (the per-piece placement + rotation the game builds). updateHatchery
// loads the pieces (FAM::loadShell), randomizes with GameController::frandom, rotates with rotate_pyramid, and
// instances them (BatchedMeshNode). Its matrix lambdas ($_2/$_3/$_5) return Eigen::Matrix<f,4,4> via the arm64
// sret out-param (pointer in x8). We seed x8=ret + read RetVec[0..15] as a column-major 4x4, exactly like
// FarmPlacementRecovery reads a Vec3. The translation column (cells 12,13,14) = the piece's position relative to
// the assembly; the rotation block + the rate constants (e.g. 1/128 spin step) = the motion.
//
// Honest: when a lambda body branches or computes via opaque calls, the recovery may capture only the literal
// translation (the common case here: the positions are baked constants). opaqueCount flags residual unknowns.
public static class HatcheryAssemblyRecovery
{
    public readonly record struct Mat4(bool Ok, string Lambda, ExprNode?[] Cells, int OpaqueCount, string Diagnostics)
    {
        // The translation column of a column-major 4x4 (cells 12,13,14), folded to constants when fully resolved.
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

    public readonly record struct Assembly(bool Ok, float[]? Anchor, IReadOnlyList<Mat4> Transforms, string Diagnostics)
    {
        public JsonObject ToJson() => new()
        {
            ["ok"] = Ok,
            ["anchor"] = Anchor is { } a ? new JsonArray(a[0], a[1], a[2]) : null,
            ["transforms"] = new JsonArray(Transforms.Select(t => (JsonNode)t.ToJson()).ToArray()),
            ["diagnostics"] = Diagnostics,
        };
    }

    private static readonly IReadOnlyDictionary<string, double> Empty = new Dictionary<string, double>();

    // The mangled tail of lambda N's call operator returning Eigen::Matrix<f,4,4>.
    private static string MatrixLambdaNeedle(string tag) =>
        $"updateHatcheryEP14GameControllerbE3{tag}FN5Eigen6MatrixIfLi4ELi4ELi0ELi4ELi4EEEvEEclEv";

    public static Assembly Recover(byte[] bin)
    {
        if (bin is null || bin.Length < 64) return new(false, null, [], "binary too short");
        if (!MachoText.TryFindText(bin, out var tfo, out _, out var tvm))
            return new(false, null, [], "no __text");
        var syms = MachoSymbols.Read(bin);

        var anchor = RecoverAnchor(bin, syms, tvm, tfo);

        var transforms = new[] { "$_2", "$_3", "$_5" }
            .Select(tag => RecoverMatrix(bin, syms, tvm, tfo, tag))
            .ToList();

        var ok = anchor is not null || transforms.Any(t => t.Ok);
        return new(ok, anchor, transforms, ok ? "ok" : "nothing recovered");
    }

    // The assembly anchor = the first Vec3 of literal float constants the main fn loads that looks like a plot
    // position (X in the farm's plot range). updateHatchery's constants start with (anchorX, anchorY, anchorZ).
    private static float[]? RecoverAnchor(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, ulong tvm, int tfo)
    {
        var ex = FunctionConstantExtractor.ExtractWith(bin, syms, ["FarmScene14updateHatcheryEP14GameControllerb"]);
        if (!ex.Ok) return null;
        // the first three constants in plot range (X ~ 5..30) = the anchor (x,y,z). Scan for the first triple where
        // the first value is a plausible plot X and the next two are small offsets.
        var f = ex.Floats;
        for (int i = 0; i + 2 < f.Count; i++)
        {
            double x = f[i], y = f[i + 1], z = f[i + 2];
            if (x is > 4 and < 40 && Math.Abs(y) < 20 && Math.Abs(z) < 20)
                return [(float)x, (float)y, (float)z];
        }
        return null;
    }

    private static Mat4 RecoverMatrix(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms, ulong tvm, int tfo, string tag)
    {
        var needle = MatrixLambdaNeedle(tag);
        if (!MachoSymbols.TryFindFunc(syms, [needle], out var fn))
            return new(false, tag, new ExprNode?[16], 0, $"symbol not found: {tag}");
        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, tvm, tfo, out var code, out _))
            return new(false, tag, new ExprNode?[16], 0, "function range out of bounds");

        // x8 = the sret out-param (the returned matrix's storage); its stores land in RetVec keyed by byte offset.
        var bases = new Dictionary<string, string> { ["x0"] = "self", ["x8"] = "ret" };
        var exec = Arm64SymbolicExecutor.Run(code, fn, tvm, tfo, syms, new Dictionary<string, ExprNode>(), bases, KnownCallModels.Resolve);

        var cells = new ExprNode?[16];
        for (int i = 0; i < 16; i++)
            cells[i] = exec.RetVec.TryGetValue(i * 4, out var c) ? ExprNode.Fold(c) : null;
        var any = cells.Any(c => c is not null);
        return new(any, tag, cells, exec.Opaque, any ? "ok" : "out-param matrix not captured");
    }
}
