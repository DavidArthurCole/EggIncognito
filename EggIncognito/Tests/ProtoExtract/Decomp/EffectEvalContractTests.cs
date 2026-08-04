using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class EffectEvalContractTests {
    [Theory]
    [InlineData("Const")]
    [InlineData("Input")]
    [InlineData("Field")]
    [InlineData("Add")]
    [InlineData("Sub")]
    [InlineData("Mul")]
    [InlineData("Div")]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("Sin")]
    [InlineData("Cos")]
    [InlineData("Sqrt")]
    [InlineData("Select")]
    [InlineData("MatrixBuild")]
    [InlineData("Opaque")]
    public void Json_UsesExpectedOpNames(string op) {
        ExprNode n = op switch {
            "Const" => new ConstExpr(1),
            "Input" => new Input("t"),
            "Field" => new Field("x8", 0x50),
            "Add" => new Binary(BinOp.Add, new ConstExpr(1), new ConstExpr(2)),
            "Sub" => new Binary(BinOp.Sub, new ConstExpr(1), new ConstExpr(2)),
            "Mul" => new Binary(BinOp.Mul, new ConstExpr(1), new ConstExpr(2)),
            "Div" => new Binary(BinOp.Div, new ConstExpr(1), new ConstExpr(2)),
            "Min" => new Binary(BinOp.Min, new ConstExpr(1), new ConstExpr(2)),
            "Max" => new Binary(BinOp.Max, new ConstExpr(1), new ConstExpr(2)),
            "Sin" => new Unary(UnOp.Sin, new ConstExpr(1)),
            "Cos" => new Unary(UnOp.Cos, new ConstExpr(1)),
            "Sqrt" => new Unary(UnOp.Sqrt, new ConstExpr(1)),
            "Select" => new SelectExpr(new ConstExpr(1), new ConstExpr(2), new ConstExpr(3)),
            "MatrixBuild" => new MatrixBuild(Enumerable.Repeat((ExprNode)new ConstExpr(0), 16).ToList()),
            _ => new Opaque("x", [])
        };
        Assert.Equal(op, (string?)ExprNode.ToJson(n)["op"]);
    }
}
