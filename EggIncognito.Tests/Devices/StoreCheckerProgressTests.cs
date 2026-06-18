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

    [Fact]
    public async Task ProgressNarratesWait_UpToDate()
    {
        // version never climbs: all PollAttempts rounds run, each emits a "waiting" line.
        var runner = new FakeRunner(_ => new ProcessResult(0, "versionName=1.0\n", ""));
        var checker = Checker(runner, attempts: 4);

        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));

        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
        // narration fired and reported the wait, but never a climb.
        Assert.NotEmpty(rounds);
        Assert.Contains(rounds, m => m.Contains("waiting", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rounds, m => m.Contains("installed 1.", StringComparison.OrdinalIgnoreCase) && m.Contains("was"));
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
            return new ProcessResult(0, "", ""); // the drive command
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
