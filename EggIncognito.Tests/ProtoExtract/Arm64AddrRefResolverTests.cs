using EggIncognito.Services.ProtoExtract;
using Xunit;

namespace EggIncognito.Tests.ProtoExtract;

// Arm64AddrRefResolver finds which function references a target VA (adrp+add), to pin a function whose own body
// changed across versions but whose closure recovered. Covers the defensive contract here (junk / too-short /
// no function-starts -> empty, never throws); real resolution is proven on the device binary.
public class Arm64AddrRefResolverTests
{
    [Fact]
    public void FindReferrers_NullOrShort_Empty()
    {
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(null!, 0x1000));
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(new byte[16], 0x1000));
    }

    [Fact]
    public void FindReferrers_NotAMacho_Empty()
    {
        var junk = new byte[256];
        for (int i = 0; i < junk.Length; i++) junk[i] = (byte)(i * 7);
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(junk, 0x1000));
    }

    [Fact]
    public void FindReferrers_MachoWithoutFunctionStarts_Empty()
    {
        // SyntheticMacho builds __TEXT + symtab but no LC_FUNCTION_STARTS, so the resolver has no boundaries.
        var text = new byte[64];
        var bin = SyntheticMacho.Build(text, []);
        Assert.Empty(Arm64AddrRefResolver.FindReferrers(bin, SyntheticMacho.TextVm));
    }
}
