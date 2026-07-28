using EggIncognito.Services.ProtoExtract.Decomp;

namespace EggIncognito.Tests.ProtoExtract.Decomp;

public class FarmPlacementRecoveryTests {
    [Fact]
    public void Recover_SymbolNotFound_NotOk() {
        var r = FarmPlacementRecovery.Recover(new byte[64], "DoesNotExist");
        Assert.False(r.Ok);
    }
}
