using System.Text.Json.Nodes;

namespace EggIncognito.Services.ProtoExtract.Decomp;


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

       
        var bases = new Dictionary<string, string> { ["x0"] = "gc", ["x8"] = "ret" };
        var exec = Arm64SymbolicExecutor.Run(code, fn, tvm, tfo, syms, new Dictionary<string, ExprNode>(), bases, KnownCallModels.Resolve);

        ExprNode? Axis(long off) => exec.RetVec.TryGetValue(off, out var e) ? FoldFarmWidth(ExprNode.Fold(e)) : null;
        var x = Axis(0); var y = Axis(4); var z = Axis(8);
        var ok = x is not null;
        var diag = ok ? "ok" : "out-param X not captured";
        return new(ok, fn.Name, x, y, z, exec.Opaque, diag);
    }

   
   
    private static ExprNode FoldFarmWidth(ExprNode n)
    {
        switch (n)
        {
            case Binary { Op: BinOp.Min } b when IsBound(b.A) && IsBound(b.B):
                return new Input("farmWidth");
            case Select s when IsBound(s.A) && IsBound(s.B):
               
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
