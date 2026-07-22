using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class EffectRecoveryTests {
    [Fact]
    public void Recover_SymbolNotFound_ReturnsNotOk() {
        var r = EffectRecovery.Recover(new byte[64], "DoesNotExist", count: null);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Recover_RealBinary_Galaxy_RecoversMath() {
        var bin = TestBinary();
        if (bin is null) return;

        var r = EffectRecovery.Recover(bin, "DrawableGalaxyParticle6updateEf", count: new Const(27));
        Assert.True(r.Ok, r.Diagnostics);
        Assert.Equal(new Const(27), r.Count);
        Assert.NotNull(r.Placement);


        Assert.True(ExprNode.Depth(r.Placement!) > 4, "placement too shallow; no real math recovered");
        var json = ExprNode.ToJson(r.Placement!).ToJsonString();
        Assert.Contains("\"op\":\"Field\"", json);
        Assert.Contains("\"v\":27", json);
    }

    private static byte[]? TestBinary() {
        foreach (var rel in new[] { "../../../../captures/egginc", "../../../../../captures/egginc", "../../../../EggIncognito/captures/egginc" }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }
        return null;
    }
}
