using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EggIncognito.Tests.Devices;

// The progress callback narrates the check (read version, nudge store, wait per round, climb/timeout) and the
// final StoreCheckResult matches the outcome. We assert behavior (climb announced, waiting reported, no throw),
// not exact callback counts, since the narration includes setup lines plus one per poll round.
// Drives AndroidPlayStoreChecker (no config dependency) with a fake adb runner. PollSeconds=0 keeps it fast.
public class StoreCheckerProgressTests
{
    sealed class FakeRunner(Func<string[], ProcessResult> fn) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct) => Task.FromResult(fn(args));
    }

    static DeviceStoreTarget Target => new("a", "android", "SER", "com.auxbrain.egginc");

    static AndroidPlayStoreChecker Checker(FakeRunner runner, int attempts) =>
        new(runner, new AndroidPlayStoreChecker.Options("am start {package}", 0, attempts),
            NullLogger<AndroidPlayStoreChecker>.Instance);

    // A minimal uiautomator dump containing an Update button (matches the A15 shape: TextView label whose
    // bounds we tap). Returned for `cat` of the dumped UI file so the drive can find + tap Update.
    const string UiWithUpdate =
        "<hierarchy><node text=\"Update\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";
    const string UiNoUpdate =
        "<hierarchy><node text=\"Open\" bounds=\"[718,551][851,608]\"/>" +
        "<node text=\"Uninstall\" bounds=\"[200,551][333,608]\"/></hierarchy>";

    [Fact]
    public async Task UpToDate_WhenNoUpdateButton()
    {
        // Play page shows Open/Uninstall but no Update -> already current -> up_to_date, no wait loop.
        var runner = new FakeRunner(args =>
        {
            if (args.Contains("dumpsys")) return new ProcessResult(0, "versionName=1.0\n", "");
            if (args.Any(a => a.Contains("cat"))) return new ProcessResult(0, UiNoUpdate, "");
            return new ProcessResult(0, "", ""); // wake/dismiss/open/dump/tap
        });
        var checker = Checker(runner, attempts: 4);

        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        Assert.NotEmpty(rounds);
        // no Update button -> never enters the install wait loop.
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProgressAnnouncesClimb_ThenUpdated()
    {
        // dumpsys: drive (no dumpsys) then probes. Climb on the 2nd poll round.
        // Call sequence of dumpsys reads: before, poll1, poll2(climb).
        var dumpsys = 0;
        var runner = new FakeRunner(args =>
        {
            if (args.Contains("dumpsys"))
            {
                var v = dumpsys++ >= 2 ? "1.1" : "1.0"; // before=1.0, poll1=1.0, poll2+=1.1
                return new ProcessResult(0, $"versionName={v}\n", "");
            }
            if (args.Any(a => a.Contains("cat"))) return new ProcessResult(0, UiWithUpdate, ""); // UI dump w/ Update
            return new ProcessResult(0, "", ""); // wake/dismiss/open/uiautomator dump/input tap
        });
        var checker = Checker(runner, attempts: 10);

        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));

        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
        // the climb is announced with both versions, before the loop returns.
        Assert.Contains(rounds, m => m.Contains("1.1") && m.Contains("1.0"));
    }

    [Fact]
    public async Task NullProgress_NoThrow()
    {
        var runner = new FakeRunner(_ => new ProcessResult(0, "versionName=1.0\n", ""));
        var checker = Checker(runner, attempts: 2);
        var result = await checker.CheckAndUpdateAsync(Target, default, null);
        Assert.Equal("up_to_date", result.Action);
    }

    [Fact]
    public void FindUpdateButtonCenter_ParsesBoundsCenter()
    {
        // [718,551][851,608] -> center ((718+851)/2, (551+608)/2) = (784, 579)
        var c = AndroidPlayStoreChecker.FindUpdateButtonCenter(UiWithUpdate);
        Assert.NotNull(c);
        Assert.Equal((784, 579), c!.Value);
    }

    [Fact]
    public void FindUpdateButtonCenter_NoUpdate_ReturnsNull()
    {
        Assert.Null(AndroidPlayStoreChecker.FindUpdateButtonCenter(UiNoUpdate));
    }

    [Fact]
    public async Task Unreachable_NeverReportsWait()
    {
        // empty dumpsys -> no version read -> unreachable before the store is driven.
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var checker = Checker(runner, attempts: 4);
        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));
        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
        // it may announce "reading version…" but must never reach the wait loop.
        Assert.DoesNotContain(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
    }
}
