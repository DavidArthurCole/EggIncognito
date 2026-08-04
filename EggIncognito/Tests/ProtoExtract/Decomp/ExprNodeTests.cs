using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class ExprNodeTests {
    [Fact]
    public void Fold_MulOfConsts_CollapsesToConst() {
        var e = new Binary(BinOp.Mul, new ConstExpr(3), new ConstExpr(4));
        Assert.Equal(new ConstExpr(12), ExprNode.Fold(e));
    }

    [Fact]
    public void Fold_AddZero_AndMulOne_Simplify() {
        Assert.Equal(new Input("t"), ExprNode.Fold(new Binary(BinOp.Add, new Input("t"), new ConstExpr(0))));
        Assert.Equal(new Input("t"), ExprNode.Fold(new Binary(BinOp.Mul, new Input("t"), new ConstExpr(1))));
        Assert.Equal(new ConstExpr(0), ExprNode.Fold(new Binary(BinOp.Mul, new Input("t"), new ConstExpr(0))));
    }

    [Fact]
    public void Fold_UnaryOfConst_Evaluates() =>
        Assert.Equal(new ConstExpr(-2.5), ExprNode.Fold(new Unary(UnOp.Neg, new ConstExpr(2.5))));

    [Fact]
    public void Fold_NestedConst_FullyReduces() {
        var e = new Binary(BinOp.Add, new Binary(BinOp.Mul, new ConstExpr(2), new ConstExpr(3)), new ConstExpr(1));
        Assert.Equal(new ConstExpr(7), ExprNode.Fold(e));
    }

    [Fact]
    public void ToJson_RoundTripsShape() {
        var e = new Binary(BinOp.Mul, new Input("t"), new ConstExpr(2));
        var j = ExprNode.ToJson(e);
        Assert.Equal("Mul", (string?)j["op"]);
        Assert.Equal("t", (string?)j["a"]!["name"]);
        Assert.Equal(2.0, (double)j["b"]!["v"]!);
    }

    [Fact]
    public void CountOpaque_CountsLeaves() {
        var e = new Binary(BinOp.Add, new Opaque("foo", []), new Opaque("bar", [new ConstExpr(1)]));
        Assert.Equal(2, ExprNode.CountOpaque(e));
    }

    [Fact]
    public void Depth_MeasuresNesting() {
        var e = new Binary(BinOp.Add, new ConstExpr(1), new Binary(BinOp.Mul, new ConstExpr(2), new ConstExpr(3)));
        Assert.Equal(3, ExprNode.Depth(e));
    }
}
