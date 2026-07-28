using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class FeedTriggerTests {
    [Fact]
    public void ProtoChanged_FiresOnlyWhenChanged() {
        Assert.True(FeedTrigger.Matches("proto_changed", true, true, ["android"], "android"));
        Assert.False(FeedTrigger.Matches("proto_changed", true, false, ["android"], "android"));
    }

    [Fact]
    public void NewVersion_FiresOnAnyNew() {
        Assert.True(FeedTrigger.Matches("new_version", true, false, ["android"], "android"));
        Assert.False(FeedTrigger.Matches("new_version", false, false, ["android"], "android"));
    }

    [Fact]
    public void PlatformFilter_Excludes() =>
        Assert.False(FeedTrigger.Matches("new_version", true, true, ["ios"], "android"));
}
