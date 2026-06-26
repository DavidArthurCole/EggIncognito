using EggIncognito.Core.Services.Devices;
using EggIncognito.Services.Devices;
using Xunit;

namespace EggIncognito.Tests.Devices;

public class DeviceJobTrackerTests
{
    // Manual TimeProvider: advanceable clock, no test package needed.
    sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 6, 18, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan d) => _now += d;
    }

    static StoreCheckResult UpToDate => new(true, "1.0", "1.0", false, false, "up_to_date", "no newer version");

    [Fact]
    public void TryStart_RejectsSecondConcurrentRunning()
    {
        var t = new DeviceJobTracker(new FakeTime());
        Assert.True(t.TryStart("dev", "checking..."));
        Assert.False(t.TryStart("dev", "checking again..."));
    }

    [Fact]
    public void TryStart_DifferentDevices_BothStart()
    {
        var t = new DeviceJobTracker(new FakeTime());
        Assert.True(t.TryStart("a", "x"));
        Assert.True(t.TryStart("b", "x"));
    }

    [Fact]
    public void Progress_UpdatesMessage_StaysRunning()
    {
        var t = new DeviceJobTracker(new FakeTime());
        t.TryStart("dev", "checking...");
        t.Progress("dev", "poll 3/24: installed 1.0");
        var s = t.Get("dev");
        Assert.NotNull(s);
        Assert.Equal(JobState.Running, s!.State);
        Assert.Equal("poll 3/24: installed 1.0", s.Message);
    }

    [Fact]
    public void Finish_TransitionsToDone_CarriesVerdict()
    {
        var t = new DeviceJobTracker(new FakeTime());
        t.TryStart("dev", "checking...");
        t.Finish("dev", UpToDate);
        var s = t.Get("dev");
        Assert.NotNull(s);
        Assert.Equal(JobState.Done, s!.State);
        Assert.Equal("up_to_date", s.Action);
        Assert.Equal("1.0", s.InstalledAfter);
    }

    [Fact]
    public void Fail_TransitionsToError()
    {
        var t = new DeviceJobTracker(new FakeTime());
        t.TryStart("dev", "checking...");
        t.Fail("dev", "ssh failed");
        var s = t.Get("dev");
        Assert.Equal(JobState.Error, s!.State);
        Assert.Equal("ssh failed", s.Message);
    }

    [Fact]
    public void Finish_AllowsRestartAfterDone()
    {
        var t = new DeviceJobTracker(new FakeTime());
        t.TryStart("dev", "checking...");
        t.Finish("dev", UpToDate);
        // A terminal entry is replaceable: a new check may start.
        Assert.True(t.TryStart("dev", "checking again..."));
    }

    [Fact]
    public void TerminalEntry_ExpiresOnReadAfterTtl()
    {
        var time = new FakeTime();
        var t = new DeviceJobTracker(time);
        t.TryStart("dev", "checking...");
        t.Finish("dev", UpToDate);
        Assert.NotNull(t.Get("dev")); // fresh: still visible
        time.Advance(TimeSpan.FromMinutes(3)); // past the 2-min TTL
        Assert.Null(t.Get("dev")); // expired -> idle
    }

    [Fact]
    public void RunningEntry_NeverExpiresOnRead()
    {
        var time = new FakeTime();
        var t = new DeviceJobTracker(time);
        t.TryStart("dev", "checking...");
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.NotNull(t.Get("dev"));
    }

    [Fact]
    public void Get_UnknownDevice_Null()
    {
        var t = new DeviceJobTracker(new FakeTime());
        Assert.Null(t.Get("nope"));
    }
}
