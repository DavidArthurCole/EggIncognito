using System.Text.Json;
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

    [Fact]
    public void MarkAsTest_Embed_AddsVisibleContentNoticeAndFooter() {
        string real = DiscordFeedPayload.Build(
            "android", "1.37.0", "111358", "72", "abcdef0123456789", true,
            "https://eggincognito.davidarthurcole.me/protos/android/111358");
        Assert.DoesNotContain(DiscordFeedPayload.TestNotice, real, StringComparison.Ordinal);

        string marked = DiscordFeedPayload.MarkAsTest(real);

        using var doc = JsonDocument.Parse(marked);
        Assert.Equal(DiscordFeedPayload.TestNotice, doc.RootElement.GetProperty("content").GetString());
        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.Equal(DiscordFeedPayload.TestNotice, embed.GetProperty("footer").GetProperty("text").GetString());
        Assert.Equal("Egg, Inc. 1.37.0 (build 111358, android)", embed.GetProperty("title").GetString());
    }

    [Fact]
    public void MarkAsTest_CustomTemplate_KeepsBodyBelowTheNotice() {
        string real = DiscordFeedPayload.Build(
            "ios", "1.37.0", "1.37.0.1", null, "abcdef0123456789", true, "https://x/y",
            "New build {{appVersion}} is up");

        string marked = DiscordFeedPayload.MarkAsTest(real);

        using var doc = JsonDocument.Parse(marked);
        Assert.Equal($"{DiscordFeedPayload.TestNotice}\nNew build 1.37.0 is up",
            doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void MarkAsTest_EverySample_CarriesTheNotice() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in FeedSamples.For(kind.Key)) {
                string marked = DiscordFeedPayload.MarkAsTest(sample.Event.BuildBody(null));
                Assert.Contains(DiscordFeedPayload.TestNotice, marked, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void MarkAsTest_NonObjectBody_ReturnedUnchanged() {
        Assert.Equal("not json", DiscordFeedPayload.MarkAsTest("not json"));
        Assert.Equal("[1,2]", DiscordFeedPayload.MarkAsTest("[1,2]"));
    }
}
