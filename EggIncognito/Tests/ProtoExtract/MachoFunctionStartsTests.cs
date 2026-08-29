using EggIncognito.Core.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;

public class MachoFunctionStartsTests {
    [Fact]
    public void Read_ReturnsEmpty_WhenNoTable() {
        byte[] bin = SyntheticMacho.Build(new byte[64], [new SyntheticMacho.Sym("__ZN1A1fEv", SyntheticMacho.TextVm)]);
        Assert.Empty(MachoFunctionStarts.Read(bin));
    }

    [Fact]
    public void Read_ReturnsEmpty_OnGarbage() => Assert.Empty(MachoFunctionStarts.Read(new byte[100]));

    [Fact]
    public void TryEnclosingStart_NoTable_False() {
        byte[] bin = SyntheticMacho.Build(new byte[64], []);
        Assert.False(MachoFunctionStarts.TryEnclosingStart(bin, SyntheticMacho.TextVm, out _, out _));
    }
}
