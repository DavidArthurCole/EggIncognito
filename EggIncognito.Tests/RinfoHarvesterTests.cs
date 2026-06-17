using EggIncognito.Services;

namespace EggIncognito.Tests;

public class RinfoHarvesterTests
{
    [Fact]
    public void Harvest_FullRinfo_ReadsAllFields()
    {
        var json = """
        { "eiUserId": "EI1", "rinfo": { "eiUserId": "EI1", "clientVersion": 72, "version": "1.35.6", "build": "111341", "platform": "IOS" } }
        """;
        var o = RinfoHarvester.TryHarvest(json);
        Assert.NotNull(o);
        Assert.Equal("IOS", o!.Platform);
        Assert.Equal("1.35.6", o.Version);
        Assert.Equal("111341", o.Build);
        Assert.Equal(72, o.ClientVersion);
    }

    [Fact]
    public void Harvest_NoRinfo_ReturnsNull()
    {
        Assert.Null(RinfoHarvester.TryHarvest("""{ "eiUserId": "EI1", "soulEggs": 5 }"""));
    }

    [Fact]
    public void Harvest_PartialRinfo_ClientVersionOnly()
    {
        var o = RinfoHarvester.TryHarvest("""{ "rinfo": { "clientVersion": 72, "platform": "IOS" } }""");
        Assert.NotNull(o);
        Assert.Equal(72, o!.ClientVersion);
        Assert.Null(o.Version);
        Assert.Null(o.Build);
    }

    [Fact]
    public void Harvest_ClientVersionAsString_Parses()
    {
        var o = RinfoHarvester.TryHarvest("""{ "rinfo": { "clientVersion": "72" } }""");
        Assert.NotNull(o);
        Assert.Equal(72, o!.ClientVersion);
    }

    [Fact]
    public void Harvest_PlatformUpperCased()
    {
        var o = RinfoHarvester.TryHarvest("""{ "rinfo": { "platform": "ios", "clientVersion": 72 } }""");
        Assert.Equal("IOS", o!.Platform);
    }

    [Fact]
    public void Harvest_NestedInRealRequest_AndroidContributor()
    {
        // rinfo is the key regardless of the enclosing message; harvest reads the top-level rinfo only.
        var o = RinfoHarvester.TryHarvest("""{ "rinfo": { "clientVersion": 71, "version": "1.35.5", "platform": "ANDROID" } }""");
        Assert.Equal(71, o!.ClientVersion);
        Assert.Equal("ANDROID", o.Platform);
    }

    [Fact]
    public void Harvest_Garbage_ReturnsNull()
    {
        Assert.Null(RinfoHarvester.TryHarvest("not json"));
        Assert.Null(RinfoHarvester.TryHarvest(""));
        Assert.Null(RinfoHarvester.TryHarvest(null));
    }

    [Fact]
    public void Harvest_RinfoEmptyObject_ReturnsNull()
    {
        Assert.Null(RinfoHarvester.TryHarvest("""{ "rinfo": { } }"""));
    }
}
