using EggIncognito.Services.ProtoExtract;

namespace EggIncognito.Tests.ProtoExtract;


public class Arm64AddrRefResolverTests {
    [Fact]
    public void FindReferrers_NullOrShort_Empty() {
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(null!, 0x1000));
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(new byte[16], 0x1000));
    }

    [Fact]
    public void FindReferrers_NotAMacho_Empty() {
        var junk = new byte[256];
        for (int i = 0; i < junk.Length; i++) junk[i] = (byte)(i * 7);
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(junk, 0x1000));
    }

    [Fact]
    public void FindReferrers_MachoWithoutFunctionStarts_Empty() {

        var text = new byte[64];
        var bin = SyntheticMacho.Build(text, []);
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(bin, SyntheticMacho.TextVm));
    }
}
