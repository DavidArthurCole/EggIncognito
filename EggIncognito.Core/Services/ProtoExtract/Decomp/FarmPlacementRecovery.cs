using System.Text.Json.Nodes;

namespace EggIncognito.Services.ProtoExtract.Decomp;

// Recovers the singleton farm-position functions (FarmScene::missionControlPos / fuelTankPos / hoaPos) as
// per-axis expression trees. These return a Vec3 via the arm64 sret out-param (a pointer in x8), and compute
// X = perElementConst + farmHalfWidth + offset where farmHalfWidth = min(gc[boundA], gc[boundB]) reads two LIVE
// GameController fields. The executor captures the out-param (RetVec) + names the gc fields; this folds the
// min-of-two-bounds into a single Input("farmWidth") the caller evaluates at the farm's actual width. The
// FORMULA is exact-extracted; only the width INPUT is approximated downstream. See the spec
// docs/superpowers/specs/2026-06-29-farm-placement-recovery-design.md. Never throws.
public static class FarmPlacementRecovery
{
    public readonly record struct Vec3Model(
        bool Ok, string Function, ExprNode? X, ExprNode? Y, ExprNode? Z, int OpaqueCount, string Diagnostics)
    {
        public JsonObject ToJson() => new()
        {
            ["ok"] = Ok,
            ["function"] = Function,
            ["x"] = X is null ? null : ExprNode.ToJson(X),
            ["y"] = Y is null ? null : ExprNode.ToJson(Y),
            ["z"] = Z is null ? null : ExprNode.ToJson(Z),
            ["opaqueCount"] = OpaqueCount,
            ["diagnostics"] = Diagnostics,
        };
    }

    // The GameController farm-bound field offsets that encode the farm half-width: missionControl/fuelTank read
    // min(gc[0x3d4], gc[0x3d8]); hoa reads gc[0x3d0] (clamped max(.,10)). Any of these folds to Input("farmWidth").
    private static readonly long[] FarmWidthFields = { 0x3d0, 0x3d4, 0x3d8 };

    public static Vec3Model Recover(byte[] bin, string needle)
    {
        if (bin is null || bin.Length < 64) return new(false, needle, null, null, null, 0, "binary too short");
        if (!MachoText.TryFindText(bin, out var tfo, out _, out var tvm))
            return new(false, needle, null, null, null, 0, "no __text");

        var syms = MachoSymbols.Read(bin);
        if (!MachoSymbols.TryFindFunc(syms, [needle], out var fn))
            return new(false, needle, null, null, null, 0, $"symbol not found: {needle}");

        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, tvm, tfo, out var code, out _))
            return new(false, fn.Name, null, null, null, 0, "function range out of bounds");

        // x0 = GameController* (named "gc" so its field loads are Field("gc", off)); x8 = the sret out-param
        // pointer (its stores land in RetVec). Straight-line execution walks past the config branch and the
        // formula store, which is later in address order, so RetVec ends holding the formula result.
        var bases = new Dictionary<string, string> { ["x0"] = "gc", ["x8"] = "ret" };
        var exec = Arm64SymbolicExecutor.Run(code, fn, tvm, tfo, syms, new Dictionary<string, ExprNode>(), bases, KnownCallModels.Resolve);

        ExprNode? Axis(long off) => exec.RetVec.TryGetValue(off, out var e) ? FoldFarmWidth(ExprNode.Fold(e)) : null;
        var x = Axis(0); var y = Axis(4); var z = Axis(8);
        var ok = x is not null;
        var diag = ok ? "ok" : "out-param X not captured";
        return new(ok, fn.Name, x, y, z, exec.Opaque, diag);
    }

    // Rewrite min(Field(gc, a), Field(gc, b)) over the two farm-bound fields into Input("farmWidth"), and a lone
    // Field(gc, boundField) into Input("farmWidth") too (some axes read one bound directly). Recurses the tree.
    private static ExprNode FoldFarmWidth(ExprNode n)
    {
        switch (n)
        {
            case Binary { Op: BinOp.Min } b when IsBound(b.A) && IsBound(b.B):
                return new Input("farmWidth");
            case Select s when IsBound(s.A) && IsBound(s.B):
                // fcsel min/max over the two bounds (the executor models fcsel as Select); treat as farmWidth.
                return new Input("farmWidth");
            case Field f when IsBoundField(f):
                return new Input("farmWidth");
            case Unary u:
                return new Unary(u.Op, FoldFarmWidth(u.X));
            case Binary b:
                return new Binary(b.Op, FoldFarmWidth(b.A), FoldFarmWidth(b.B));
            case Select s:
                return new Select(FoldFarmWidth(s.Cond), FoldFarmWidth(s.A), FoldFarmWidth(s.B));
            default:
                return n;
        }
    }

    private static bool IsBound(ExprNode n) => n is Field f && IsBoundField(f);
    private static bool IsBoundField(Field f) => f.Base == "gc" && Array.IndexOf(FarmWidthFields, f.Offset) >= 0;
}
