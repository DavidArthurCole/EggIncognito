using System;
using EggIncognito.Bot;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class BotEmbedsTests
{
    private static StatusSnapshot Snap() => new(
        Mode: "Local", CanCapture: true, CanWrite: true,
        CaptureState: "Running", CaptureRunning: true,
        FlowsCaptured: 42, DeviceCount: 2, BytesCaptured: 123456,
        DbEnabled: true, SigningReady: false,
        Uptime: TimeSpan.FromMinutes(90),
        Build: BuildInfo.Parse("1.1.0+deadbeef0000", "https://github.com/DavidArthurCole/EggIncognito"),
        EndpointsOk: 10, EndpointsEmpty: 3, EndpointsMissing: 1);

    [Fact]
    public void Status_ContainsModeCaptureAndCounts()
    {
        var e = BotEmbeds.Status(Snap());
        var blob = e.Title + " " + string.Join(" ", System.Linq.Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("Local", blob);
        Assert.Contains("Running", blob);
        Assert.Contains("42", blob);
    }

    [Fact]
    public void Verify_LinksTheCommit()
    {
        var e = BotEmbeds.Verify(Snap());
        var blob = string.Join(" ", System.Linq.Enumerable.Select(e.Fields, f => f.Value.ToString()));
        Assert.Contains("deadbeef0000", blob);
        Assert.Contains("/commit/deadbeef0000", blob);
    }

    [Fact]
    public void Endpoints_ShowsCounts()
    {
        var e = BotEmbeds.Endpoints(Snap());
        var blob = string.Join(" ", System.Linq.Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("10", blob);
        Assert.Contains("3", blob);
        Assert.Contains("1", blob);
    }

    [Fact]
    public void Health_HasUptime()
    {
        var e = BotEmbeds.Health(TimeSpan.FromMinutes(5));
        Assert.Contains("pong", (e.Title + e.Description).ToLowerInvariant());
    }

    [Fact]
    public void UpdateAlreadyCurrent_ShowsHash_Blurple()
    {
        var e = BotEmbeds.UpdateAlreadyCurrent("aaaaaaaaaaaa");
        Assert.Equal("Already up to date.", e.Title);
        Assert.Equal(0x5865F2u, e.Color!.Value.RawValue);
        Assert.Contains("aaaaaaaaaaaa", e.Fields[0].Value);
    }

    [Fact]
    public void UpdateSuccess_ShowsFromTo_Green()
    {
        var e = BotEmbeds.UpdateSuccess("aaaaaaaaaaaa", "cccccccccccc");
        Assert.Equal("Updated", e.Title);
        Assert.Equal(0x57F287u, e.Color!.Value.RawValue);
        var blob = string.Join(" ", System.Linq.Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("aaaaaaaaaaaa", blob);
        Assert.Contains("cccccccccccc", blob);
    }

    [Fact]
    public void UpdateSuccess_EmptyHash_RendersUnknown()
    {
        var e = BotEmbeds.UpdateSuccess(null, "cccccccccccc");
        Assert.Contains("unknown", e.Fields[0].Value);
    }

    [Fact]
    public void UpdateSuccess_WithUrls_LinksChips()
    {
        var e = BotEmbeds.UpdateSuccess("1111111", "9999999", "https://x/commit/1", "https://x/commit/9");
        Assert.Equal("[`1111111`](https://x/commit/1)", e.Fields[0].Value);
        Assert.Equal("[`9999999`](https://x/commit/9)", e.Fields[1].Value);
    }

    [Fact]
    public void UpdateAlreadyCurrent_WithUrl_LinksChip()
    {
        var e = BotEmbeds.UpdateAlreadyCurrent("1111111", "https://x/commit/1");
        Assert.Equal("[`1111111`](https://x/commit/1)", e.Fields[0].Value);
    }

    [Fact]
    public void UpdateFailure_ShowsTailInCodeBlock_Red()
    {
        var e = BotEmbeds.UpdateFailure("docker pull: boom");
        Assert.Equal("Update failed.", e.Title);
        Assert.Equal(0xED4245u, e.Color!.Value.RawValue);
        Assert.Contains("```", e.Description);
        Assert.Contains("docker pull: boom", e.Description);
    }
}
