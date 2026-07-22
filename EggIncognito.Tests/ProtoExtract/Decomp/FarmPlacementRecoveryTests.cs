using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class FarmPlacementRecoveryTests {
    [Fact]
    public void Recover_SymbolNotFound_NotOk() {
        var r = FarmPlacementRecovery.Recover(new byte[64], "DoesNotExist");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Recover_RealBinary_MissionControl_RecoversFormula() {
        var bin = TestBinary();
        if (bin is null) return;

        var r = FarmPlacementRecovery.Recover(bin, "FarmScene17missionControlPos");
        Assert.True(r.Ok, r.Diagnostics);
        Assert.NotNull(r.X);


        var xjson = ExprNode.ToJson(r.X!).ToJsonString();
        Assert.Contains("\"name\":\"farmWidth\"", xjson);

        Assert.True(ContainsConstNear(r.X!, 2.8) || ContainsConstNear(r.X!, 1.5),
            "expected the recovered placement constants (2.8 / 1.5)");
    }

    private static bool ContainsConstNear(ExprNode n, double v) => n switch {
        Const c => Math.Abs(c.V - v) < 0.05,
        Unary u => ContainsConstNear(u.X, v),
        Binary b => ContainsConstNear(b.A, v) || ContainsConstNear(b.B, v),
        Select s => ContainsConstNear(s.Cond, v) || ContainsConstNear(s.A, v) || ContainsConstNear(s.B, v),
        _ => false,
    };

    private static byte[]? TestBinary() {
        foreach (var rel in new[] { "../../../../captures/egginc", "../../../../../captures/egginc", "../../../../EggIncognito/captures/egginc" }) {
            var full = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
            if (File.Exists(full)) return File.ReadAllBytes(full);
        }
        return null;
    }
}
