using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EggIncognito.Tests.Devices;

// The progress callback fires once per poll round, and the final StoreCheckResult matches the climb outcome.
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
    public async Task ProgressFiresPerRound_UpToDate()
    {
        // version never climbs: all PollAttempts rounds run, callback fires once each.
        var runner = new FakeRunner(_ => new ProcessResult(0, "versionName=1.0\n", ""));
        var checker = Checker(runner, attempts: 4);

        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));

        Assert.Equal(4, rounds.Count);
        Assert.Equal("up_to_date", result.Action);
        Assert.False(result.Installed);
    }

    [Fact]
    public async Task ProgressFiresUntilClimb_ThenUpdated()
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

        Assert.Equal(2, rounds.Count); // climbed on round 2, loop returns
        Assert.Equal("updated", result.Action);
        Assert.True(result.Installed);
        Assert.Equal("1.0", result.InstalledBefore);
        Assert.Equal("1.1", result.InstalledAfter);
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
    public async Task Unreachable_NoProgress()
    {
        // empty dumpsys -> no version read -> unreachable before any poll.
        var runner = new FakeRunner(_ => new ProcessResult(0, "", ""));
        var checker = Checker(runner, attempts: 4);
        var rounds = new List<string>();
        var result = await checker.CheckAndUpdateAsync(Target, default, msg => rounds.Add(msg));
        Assert.Empty(rounds);
        Assert.Equal("unreachable", result.Action);
        Assert.False(result.Reachable);
    }
}
