using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests.Devices;

public class StoreCheckerProgressTests {
    private const string UiWithUpdate =
        "<hierarchy><node text=\"Update\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    private const string UiNoUpdate =
        "<hierarchy><node text=\"Open\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    private static DeviceStoreTarget Target => new("a", "android", "SER", "com.auxbrain.egginc");

    private static AndroidPlayStoreChecker Checker(FakeRunner runner, int attempts) =>
        new(runner, new AndroidPlayStoreChecker.Options("am start {package}", 0, attempts,
                0, 0),
            NullLogger<AndroidPlayStoreChecker>.Instance);

    [Fact]
    public async Task UpToDate_WhenNoUpdateButton() {
        var runner = new FakeRunner(args => {
            return args.Contains("dumpsys")
                ? new ProcessResult(0, "versionName=1.0\n", "")
                : args.Any(a => a.Contains("cat"))
                    ? new ProcessResult(0, UiNoUpdate, "")
                    : new ProcessResult(0, "", "");
        });
        var checker = Checker(runner, 4);

        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        Assert.NotEmpty(rounds);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProgressAnnouncesClimb_ThenUpdated() {
        int dumpsys = 0;
        var runner = new FakeRunner(args => {
            if (args.Contains("dumpsys")) {
                string v = dumpsys++ >= 2 ? "1.1" : "1.0";
                return new ProcessResult(0, $"versionName={v}\n", "");
            }

            return args.Any(a => a.Contains("cat"))
                ? new ProcessResult(0, UiWithUpdate, "")
                : new ProcessResult(0, "", "");
        });
        var checker = Checker(runner, 10);

        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));

        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
        Assert.Contains(rounds, m => m.Contains("1.1") && m.Contains("1.0"));
    }

    [Fact]
    public async Task NullProgress_NoThrow() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "versionName=1.0\n", ""));
        var checker = Checker(runner, 2);
        var result = await checker.CheckAndUpdateAsync(Target, default);
        Assert.Equal("up_to_date", result.Action);
    }

    [Fact]
    public void FindUpdateButtonCenter_ParsesBoundsCenter() {
        var c = AndroidPlayStoreChecker.FindUpdateButtonCenter(UiWithUpdate);
        Assert.NotNull(c);
        Assert.Equal((784, 579), c.Value);
    }

    [Fact]
    public void FindUpdateButtonCenter_NoUpdate_ReturnsNull() =>
        Assert.Null(AndroidPlayStoreChecker.FindUpdateButtonCenter(UiNoUpdate));

    [Fact]
    public async Task Unreachable_NeverReportsWait() {
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var checker = Checker(runner, 4);
        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));
        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) =>
            Task.FromResult(fn(args));
    }
}
