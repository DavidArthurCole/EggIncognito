using EggIncognito.Services.Backfill;

namespace EggIncognito.Tests;

public class SourcePrecedenceTests
{
    [Fact]
    public void Farm_NotOverwrittenByElgranjero() =>
        Assert.False(SourcePrecedence.MayOverwriteProto("farm", "elgranjero"));

    [Fact]
    public void Elgranjero_OverwritesEmptyOrSelf() =>
        Assert.True(SourcePrecedence.MayOverwriteProto("elgranjero", "elgranjero"));

    [Fact]
    public void Store_NeverSetsProto() =>
        Assert.False(SourcePrecedence.MayOverwriteProto("elgranjero", "playstore"));

    [Fact]
    public void AppStore_NeverSetsProto() =>
        Assert.False(SourcePrecedence.MayOverwriteProto("farm", "appstore"));

    [Fact]
    public void Farm_OverwritesFarm() =>
        Assert.True(SourcePrecedence.MayOverwriteProto("farm", "farm"));
}
