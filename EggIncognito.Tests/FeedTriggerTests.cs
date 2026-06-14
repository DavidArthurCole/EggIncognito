using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class FeedTriggerTests
{
    [Fact]
    public void ProtoChanged_FiresOnlyWhenChanged()
    {
        Assert.True(FeedTrigger.Matches("proto_changed", created: true, protoChanged: true, subPlatforms: ["android"], evtPlatform: "android"));
        Assert.False(FeedTrigger.Matches("proto_changed", created: true, protoChanged: false, ["android"], "android"));
    }

    [Fact]
    public void NewVersion_FiresOnAnyNew()
    {
        Assert.True(FeedTrigger.Matches("new_version", created: true, protoChanged: false, ["android"], "android"));
        Assert.False(FeedTrigger.Matches("new_version", created: false, protoChanged: false, ["android"], "android"));
    }

    [Fact]
    public void PlatformFilter_Excludes()
    {
        Assert.False(FeedTrigger.Matches("new_version", true, true, ["ios"], "android"));
    }
}
