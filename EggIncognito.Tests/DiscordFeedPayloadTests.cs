using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class DiscordFeedPayloadTests
{
    [Fact]
    public void Build_Changed_ContainsVersionLabelBuildAndShortSha()
    {
        var json = DiscordFeedPayload.Build(
            "android", "1.99.0", "111343", "72", "abcdef0123456789deadbeef", protoChanged: true,
            "https://eggincognito.davidarthurcole.me/protos/android/111343");

        Assert.Contains("Egg, Inc. 1.99.0 (build 111343, android)", json); // app version + build + platform
        Assert.Contains("changed", json);
        Assert.Contains("72", json); // client version field present when known
        Assert.Contains("abcdef012345", json); // 12-char short sha
        Assert.DoesNotContain("deadbeef", json); // tail truncated off
    }

    [Fact]
    public void Build_Unchanged_LabelsUnchanged()
    {
        var json = DiscordFeedPayload.Build(
            "ios", "1.99.0", "111343", clientVersion: null, "shortsha", protoChanged: false, "https://x/y");
        Assert.Contains("unchanged", json);
        Assert.Contains("shortsha", json); // sub-12-char sha passes through whole
        Assert.DoesNotContain("Client", json); // null client version omits the field
    }

    [Fact]
    public void BuildPageUrl_DefaultsToMainHost_NotAbandonedSubdomain()
    {
        var url = FeedDispatcher.BuildPageUrl(null, "android", "111343");
        Assert.Equal("https://eggincognito.davidarthurcole.me/protos/android/111343", url);
        Assert.DoesNotContain("protos.eggincognito", url); // abandoned subdomain must not reappear
    }

    [Fact]
    public void BuildPageUrl_HonorsConfiguredBaseUrl_TrimmingSlash()
    {
        var url = FeedDispatcher.BuildPageUrl("https://example.test/", "ios", "1.36.0.2");
        Assert.Equal("https://example.test/protos/ios/1.36.0.2", url);
    }
}
