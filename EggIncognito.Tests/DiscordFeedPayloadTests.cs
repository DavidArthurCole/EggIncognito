using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class DiscordFeedPayloadTests {
    [Fact]
    public void Build_Changed_ContainsVersionLabelBuildAndShortSha() {
        string json = DiscordFeedPayload.Build(
            "android", "1.99.0", "111343", "72", "abcdef0123456789deadbeef", true,
            "https://eggincognito.davidarthurcole.me/protos/android/111343");

        Assert.Contains("Egg, Inc. 1.99.0 (build 111343, android)", json);
        Assert.Contains("changed", json);
        Assert.Contains("72", json);
        Assert.Contains("abcdef012345", json);
        Assert.DoesNotContain("deadbeef", json);
    }

    [Fact]
    public void Build_Unchanged_LabelsUnchanged() {
        string json = DiscordFeedPayload.Build(
            "ios", "1.99.0", "111343", null, "shortsha", false, "https://x/y");
        Assert.Contains("unchanged", json);
        Assert.Contains("shortsha", json);
        Assert.DoesNotContain("Client", json);
    }

    [Fact]
    public void BuildPageUrl_DefaultsToMainHost_NotAbandonedSubdomain() {
        string url = FeedDispatcher.BuildPageUrl(null, "android", "111343");
        Assert.Equal("https://eggincognito.davidarthurcole.me/protos/android/111343", url);
        Assert.DoesNotContain("protos.eggincognito", url);
    }

    [Fact]
    public void BuildPageUrl_HonorsConfiguredBaseUrl_TrimmingSlash() {
        string url = FeedDispatcher.BuildPageUrl("https://example.test/", "ios", "1.36.0.2");
        Assert.Equal("https://example.test/protos/ios/1.36.0.2", url);
    }
}
