using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class EffectRecoveryTests {
    [Fact]
    public void Recover_SymbolNotFound_ReturnsNotOk() {
        var r = EffectRecovery.Recover(new byte[64], "DoesNotExist", null);
        Assert.False(r.Ok);
    }
}
