using EggIncognito.Bot;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class BotEmbedsTests {
    private static StatusSnapshot Snap() => new(
        Mode: "Local", CanCapture: true, CanWrite: true,
        CaptureState: "Running", CaptureRunning: true,
        FlowsCaptured: 42, DeviceCount: 2, BytesCaptured: 123456,
        DbEnabled: true, SigningReady: false,
        Uptime: TimeSpan.FromMinutes(90),
        Build: BuildInfo.Parse("1.1.0+deadbeef0000", "https://github.com/DavidArthurCole/EggIncognito"),
        EndpointsOk: 10, EndpointsEmpty: 3, EndpointsMissing: 1);

    [Fact]
    public void Status_ContainsModeCaptureAndCounts() {
        var e = BotEmbeds.Status(Snap());
        var blob = e.Title + " " + string.Join(" ", System.Linq.Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("Local", blob);
        Assert.Contains("Running", blob);
        Assert.Contains("42", blob);
    }

    [Fact]
    public void Endpoints_ShowsCounts() {
        var e = BotEmbeds.Endpoints(Snap());
        var blob = string.Join(" ", System.Linq.Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("10", blob);
        Assert.Contains("3", blob);
        Assert.Contains("1", blob);
    }

    [Fact]
    public void Health_HasUptime() {
        var e = BotEmbeds.Health(TimeSpan.FromMinutes(5));
        Assert.Contains("pong", (e.Title + e.Description).ToLowerInvariant());
    }

}
