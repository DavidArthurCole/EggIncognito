using System.Text.Json.Nodes;

namespace EggIncognito.Services.ProtoExtract.Decomp;


public static class EffectRecovery {
    public readonly record struct EffectModel(
        bool Ok, string Effect, ExprNode? Count, ExprNode? Placement, ExprNode? Size, int OpaqueCount, string Diagnostics) {
        public JsonObject ToJson() => new() {
            ["ok"] = Ok,
            ["effect"] = Effect,
            ["count"] = Count is null ? null : ExprNode.ToJson(Count),
            ["placement"] = Placement is null ? null : ExprNode.ToJson(Placement),
            ["size"] = Size is null ? null : ExprNode.ToJson(Size),
            ["opaqueCount"] = OpaqueCount,
            ["diagnostics"] = Diagnostics,
        };
    }

    public static EffectModel Recover(byte[] bin, string updateNeedle, string spawnNeedle, ExprNode? count) {
        if (bin is null || bin.Length < 64)
            return new(false, "", count, null, null, 0, "binary too short");
        if (!MachoText.TryFindText(bin, out var tfo, out _, out var tvm))
            return new(false, "", count, null, null, 0, "no __text");

        var syms = MachoSymbols.Read(bin);
        if (!MachoSymbols.TryFindFunc(syms, [updateNeedle], out var fn))
            return new(false, "", count, null, null, 0, $"symbol not found: {updateNeedle}");

        if (!Arm64Decode.SliceFunction(bin, fn.Start, fn.End, tvm, tfo, out var code, out _))
            return new(false, "", count, null, null, 0, "function range out of bounds");

        var seed = new Dictionary<string, ExprNode> { ["s0"] = new Input("t"), ["x1"] = new Input("particleIndex") };
        var exec = Arm64SymbolicExecutor.Run(code, fn, tvm, tfo, syms, seed, KnownCallModels.Resolve);



        ExprNode? placement = null;
        if (exec.SinkStackPtr is { } baseOff) {
            var cells = new ExprNode[16];
            for (int i = 0; i < 16; i++)
                cells[i] = exec.Stack.TryGetValue(baseOff + i * 4, out var c) ? ExprNode.Fold(c) : new Const(0);
            placement = new MatrixBuild(cells);
        } else if (exec.SinkArg is not null) {
            placement = ExprNode.Fold(exec.SinkArg);
        }

        var size = exec.Regs.TryGetValue("v1", out var s) ? ExprNode.Fold(s) : null;
        var diag = exec.Diagnostics + (placement is null ? "; no sink captured (addParticle not reached)" : "");
        return new(placement is not null, "galaxy-particle", count, placement, size, exec.Opaque, diag);
    }
}
