using System.Text.Json.Nodes;

namespace EggIncognito.Services.ProtoExtract.Decomp;

// Immutable expression-tree IR: the portable form of a recovered function's math. The symbolic executor emits
// it, EffectRecovery serializes it to JSON, and the JS evaluator replays it per frame. Pure data + a fold pass
// that collapses constant subtrees (so a fully-constant function reduces to a number, matching the cheap
// constant path) and applies algebraic identities. Opaque = an honest black box for unmodeled calls/instrs.
public enum UnOp { Neg, Sin, Cos, Sqrt, Abs, Floor }
public enum BinOp { Add, Sub, Mul, Div, Min, Max, Mod }

public abstract record ExprNode
{
    public static ExprNode Fold(ExprNode n) => n switch
    {
        Unary u => FoldUnary(u.Op, Fold(u.X)),
        Binary b => FoldBinary(b.Op, Fold(b.A), Fold(b.B)),
        Select s => new Select(Fold(s.Cond), Fold(s.A), Fold(s.B)),
        Vec v => new Vec(v.Lanes.Select(Fold).ToList()),
        Index ix => new Index(Fold(ix.Vec), ix.Lane),
        MatrixBuild m => new MatrixBuild(m.Cells.Select(Fold).ToList()),
        Opaque o => new Opaque(o.Call, o.Args.Select(Fold).ToList()),
        _ => n,
    };

    private static ExprNode FoldUnary(UnOp op, ExprNode x) => x is Const c
        ? new Const(op switch
        {
            UnOp.Neg => -c.V, UnOp.Sin => Math.Sin(c.V), UnOp.Cos => Math.Cos(c.V),
            UnOp.Sqrt => Math.Sqrt(c.V), UnOp.Abs => Math.Abs(c.V), UnOp.Floor => Math.Floor(c.V), _ => c.V,
        })
        : new Unary(op, x);

    private static ExprNode FoldBinary(BinOp op, ExprNode a, ExprNode b)
    {
        if (a is Const ca && b is Const cb)
            return new Const(op switch
            {
                BinOp.Add => ca.V + cb.V, BinOp.Sub => ca.V - cb.V, BinOp.Mul => ca.V * cb.V,
                BinOp.Div => cb.V == 0 ? 0 : ca.V / cb.V, BinOp.Min => Math.Min(ca.V, cb.V),
                BinOp.Max => Math.Max(ca.V, cb.V), BinOp.Mod => cb.V == 0 ? 0 : ca.V % cb.V, _ => 0,
            });
        if (op == BinOp.Add && b is Const { V: 0 }) return a;
        if (op == BinOp.Add && a is Const { V: 0 }) return b;
        if (op == BinOp.Sub && b is Const { V: 0 }) return a;
        if (op == BinOp.Mul && (a is Const { V: 0 } || b is Const { V: 0 })) return new Const(0);
        if (op == BinOp.Mul && b is Const { V: 1 }) return a;
        if (op == BinOp.Mul && a is Const { V: 1 }) return b;
        if (op == BinOp.Div && b is Const { V: 1 }) return a;
        return new Binary(op, a, b);
    }

    public static int CountOpaque(ExprNode n) => n switch
    {
        Opaque o => 1 + o.Args.Sum(CountOpaque),
        Unary u => CountOpaque(u.X),
        Binary b => CountOpaque(b.A) + CountOpaque(b.B),
        Select s => CountOpaque(s.Cond) + CountOpaque(s.A) + CountOpaque(s.B),
        Vec v => v.Lanes.Sum(CountOpaque),
        Index ix => CountOpaque(ix.Vec),
        MatrixBuild m => m.Cells.Sum(CountOpaque),
        _ => 0,
    };

    public static int Depth(ExprNode n) => n switch
    {
        Unary u => 1 + Depth(u.X),
        Binary b => 1 + Math.Max(Depth(b.A), Depth(b.B)),
        Select s => 1 + Math.Max(Depth(s.Cond), Math.Max(Depth(s.A), Depth(s.B))),
        Vec v => 1 + (v.Lanes.Count == 0 ? 0 : v.Lanes.Max(Depth)),
        Index ix => 1 + Depth(ix.Vec),
        MatrixBuild m => 1 + (m.Cells.Count == 0 ? 0 : m.Cells.Max(Depth)),
        Opaque o => 1 + (o.Args.Count == 0 ? 0 : o.Args.Max(Depth)),
        _ => 1,
    };

    public static JsonNode ToJson(ExprNode n) => n switch
    {
        Const c => new JsonObject { ["op"] = "Const", ["v"] = c.V },
        Input i => new JsonObject { ["op"] = "Input", ["name"] = i.Name },
        Field f => new JsonObject { ["op"] = "Field", ["base"] = f.Base, ["offset"] = f.Offset },
        Unary u => new JsonObject { ["op"] = u.Op.ToString(), ["x"] = ToJson(u.X) },
        Binary b => new JsonObject { ["op"] = b.Op.ToString(), ["a"] = ToJson(b.A), ["b"] = ToJson(b.B) },
        Select s => new JsonObject { ["op"] = "Select", ["cond"] = ToJson(s.Cond), ["a"] = ToJson(s.A), ["b"] = ToJson(s.B) },
        Vec v => new JsonObject { ["op"] = "Vec", ["lanes"] = new JsonArray(v.Lanes.Select(ToJson).ToArray()) },
        Index ix => new JsonObject { ["op"] = "Index", ["vec"] = ToJson(ix.Vec), ["lane"] = ix.Lane },
        MatrixBuild m => new JsonObject { ["op"] = "MatrixBuild", ["cells"] = new JsonArray(m.Cells.Select(ToJson).ToArray()) },
        Opaque o => new JsonObject { ["op"] = "Opaque", ["call"] = o.Call, ["args"] = new JsonArray(o.Args.Select(ToJson).ToArray()) },
        _ => new JsonObject { ["op"] = "Unknown" },
    };
}

public sealed record Const(double V) : ExprNode;
public sealed record Input(string Name) : ExprNode;
public sealed record Field(string Base, long Offset) : ExprNode;
public sealed record Unary(UnOp Op, ExprNode X) : ExprNode;
public sealed record Binary(BinOp Op, ExprNode A, ExprNode B) : ExprNode;
public sealed record Select(ExprNode Cond, ExprNode A, ExprNode B) : ExprNode;
public sealed record Vec(IReadOnlyList<ExprNode> Lanes) : ExprNode;
public sealed record Index(ExprNode Vec, int Lane) : ExprNode;
public sealed record MatrixBuild(IReadOnlyList<ExprNode> Cells) : ExprNode;
public sealed record Opaque(string Call, IReadOnlyList<ExprNode> Args) : ExprNode;
