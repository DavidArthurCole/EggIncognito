using EggIncognito.Bot;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class BotEmbedsTests {
    private static StatusSnapshot Snap() => new(
        "Local", true, true,
        "Running", true,
        42, 2, 123456,
        true, false,
        TimeSpan.FromMinutes(90),
        BuildInfo.Parse("1.1.0+deadbeef0000", "https://github.com/EggIncTools/EggIncognito"),
        10, 3, 1);

    [Fact]
    public void Status_ContainsModeCaptureAndCounts() {
        var e = BotEmbeds.Status(Snap());
        string blob = e.Title + " " + string.Join(" ", Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("Local", blob);
        Assert.Contains("Running", blob);
        Assert.Contains("42", blob);
    }

    [Fact]
    public void Endpoints_ShowsCounts() {
        var e = BotEmbeds.Endpoints(Snap());
        string blob = string.Join(" ", Enumerable.Select(e.Fields, f => f.Name + "=" + f.Value));
        Assert.Contains("10", blob);
        Assert.Contains("3", blob);
        Assert.Contains("1", blob);
    }
}
