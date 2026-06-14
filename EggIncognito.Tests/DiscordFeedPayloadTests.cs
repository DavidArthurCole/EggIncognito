using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class DiscordFeedPayloadTests
{
    [Fact]
    public void Build_Changed_ContainsVersionLabelAndShortSha()
    {
        var json = DiscordFeedPayload.Build(
            "android", "1.99.0", "abcdef0123456789deadbeef", protoChanged: true,
            "https://protos.eggincognito.davidarthurcole.me/protos/android/1.99.0");

        Assert.Contains("1.99.0", json);
        Assert.Contains("changed", json);
        Assert.Contains("abcdef012345", json); // 12-char short sha
        Assert.DoesNotContain("deadbeef", json); // tail truncated off
    }

    [Fact]
    public void Build_Unchanged_LabelsUnchanged()
    {
        var json = DiscordFeedPayload.Build("ios", "1.99.0", "shortsha", protoChanged: false, "https://x/y");
        Assert.Contains("unchanged", json);
        Assert.Contains("shortsha", json); // sub-12-char sha passes through whole
    }
}
