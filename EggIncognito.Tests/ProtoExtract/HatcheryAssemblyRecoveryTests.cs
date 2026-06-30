using EggIncognito.Services.ProtoExtract.Decomp;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

// HatcheryAssemblyRecovery reads FarmScene::updateHatchery's anchor + each matrix lambda's returned 4x4 (sret).
// The real recovery is exercised by the env-gated real-binary test; here we cover the defensive contract + the
// Mat4 translation/JSON shape without a binary.
public class HatcheryAssemblyRecoveryTests
{
    [Fact]
    public void Recover_ShortBinary_NotOk()
    {
        var a = HatcheryAssemblyRecovery.Recover(new byte[16]);
        Assert.False(a.Ok);
    }

    [Fact]
    public void Mat4_Translation_ReadsCells12_13_14()
    {
        var cells = new ExprNode?[16];
        cells[12] = new Const(13.651);
        cells[13] = new Const(4.342);
        cells[14] = new Const(2.968);
        var m = new HatcheryAssemblyRecovery.Mat4(true, "$_2", cells, 0, "ok");
        var t = m.Translation();
        Assert.NotNull(t);
        Assert.Equal(13.651f, t![0], 3);
        Assert.Equal(4.342f, t[1], 3);
        Assert.Equal(2.968f, t[2], 3);
    }

    [Fact]
    public void Mat4_Translation_NullWhenCellsMissing()
    {
        var m = new HatcheryAssemblyRecovery.Mat4(true, "$_3", new ExprNode?[16], 0, "ok");
        Assert.Null(m.Translation());
    }

    [Fact]
    public void Mat4_ToJson_ShapesTranslation()
    {
        var cells = new ExprNode?[16];
        cells[12] = new Const(1); cells[13] = new Const(2); cells[14] = new Const(3);
        var json = new HatcheryAssemblyRecovery.Mat4(true, "$_2", cells, 1, "ok").ToJson();
        Assert.True((bool)json["ok"]!);
        Assert.Equal("$_2", (string)json["lambda"]!);
        Assert.NotNull(json["translation"]);
    }
}
