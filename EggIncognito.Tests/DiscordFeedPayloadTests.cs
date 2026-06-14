using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class DiscordFeedPayloadTests
{
    [Fact]
    public void Build_Changed_ContainsVersionLabelBuildAndShortSha()
    {
        var json = DiscordFeedPayload.Build(
            "android", "1.99.0", "111343", "72", "abcdef0123456789deadbeef", protoChanged: true,
            "https://protos.eggincognito.davidarthurcole.me/protos/android/111343");

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
}
