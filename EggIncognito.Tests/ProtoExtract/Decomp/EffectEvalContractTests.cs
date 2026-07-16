using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class EffectEvalContractTests
{
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
    public void Json_UsesExpectedOpNames(string op)
    {
        ExprNode n = op switch
        {
            "Const" => new Const(1),
            "Input" => new Input("t"),
            "Field" => new Field("x8", 0x50),
            "Add" => new Binary(BinOp.Add, new Const(1), new Const(2)),
            "Sub" => new Binary(BinOp.Sub, new Const(1), new Const(2)),
            "Mul" => new Binary(BinOp.Mul, new Const(1), new Const(2)),
            "Div" => new Binary(BinOp.Div, new Const(1), new Const(2)),
            "Min" => new Binary(BinOp.Min, new Const(1), new Const(2)),
            "Max" => new Binary(BinOp.Max, new Const(1), new Const(2)),
            "Sin" => new Unary(UnOp.Sin, new Const(1)),
            "Cos" => new Unary(UnOp.Cos, new Const(1)),
            "Sqrt" => new Unary(UnOp.Sqrt, new Const(1)),
            "Select" => new Select(new Const(1), new Const(2), new Const(3)),
            "MatrixBuild" => new MatrixBuild(Enumerable.Repeat((ExprNode)new Const(0), 16).ToList()),
            _ => new Opaque("x", []),
        };
        Assert.Equal(op, (string?)ExprNode.ToJson(n)["op"]);
    }
}
