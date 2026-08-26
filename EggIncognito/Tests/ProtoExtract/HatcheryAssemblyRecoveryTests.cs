using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract;

public class HatcheryAssemblyRecoveryTests {
    [Fact]
    public void Recover_ShortBinary_NotOk() {
        var a = HatcheryAssemblyRecovery.Recover(new byte[16]);
        Assert.False(a.Ok);
    }

    [Fact]
    public void Mat4_Translation_ReadsCells12_13_14() {
        var cells = new ExprNode?[16];
        cells[12] = new ConstExpr(13.651);
        cells[13] = new ConstExpr(4.342);
        cells[14] = new ConstExpr(2.968);
        var m = new HatcheryAssemblyRecovery.Mat4(true, "$_2", cells, 0, "ok");
        float[]? t = m.Translation();
        Assert.NotNull(t);
        Assert.Equal(13.651f, t[0], 3);
        Assert.Equal(4.342f, t[1], 3);
        Assert.Equal(2.968f, t[2], 3);
    }

    [Fact]
    public void Mat4_Translation_NullWhenCellsMissing() {
        var m = new HatcheryAssemblyRecovery.Mat4(true, "$_3", new ExprNode?[16], 0, "ok");
        Assert.Null(m.Translation());
    }

    [Fact]
    public void Mat4_ToJson_ShapesTranslation() {
        var cells = new ExprNode?[16];
        cells[12] = new ConstExpr(1);
        cells[13] = new ConstExpr(2);
        cells[14] = new ConstExpr(3);
        var json = new HatcheryAssemblyRecovery.Mat4(true, "$_2", cells, 1, "ok").ToJson();
        Assert.True((bool)json["ok"]!);
        Assert.Equal("$_2", (string)json["lambda"]!);
        Assert.NotNull(json["translation"]);
    }

    [Fact]
    public void Timing_ToJson_CarriesTweenArgs() {
        var json = new HatcheryAssemblyRecovery.Timing(0.5f, false, 30f, 3, "ok").ToJson();
        Assert.Equal(0.5f, (float)json["waitFor"]!, 3);
        Assert.False((bool)json["waitForRandom"]!);
        Assert.Equal(30f, (float)json["smoothDuration"]!, 3);
        Assert.Equal(3, (int)json["orbitSegments"]!);
    }

    [Fact]
    public void Assembly_ToJson_IncludesTimingObject() {
        var timing = new HatcheryAssemblyRecovery.Timing(0.5f, false, 30f, 3, "ok");
        var asm = new HatcheryAssemblyRecovery.Assembly(true, [11.319f, 2.15f, 2.997f], [], timing, "ok");
        var json = asm.ToJson();
        Assert.NotNull(json["timing"]);
        Assert.Equal(3, (int)json["timing"]!["orbitSegments"]!);
    }
}
