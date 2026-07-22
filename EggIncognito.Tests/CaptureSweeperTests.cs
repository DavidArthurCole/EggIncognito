using EggIncognito.Capture;
using EggIncognito.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public class CaptureSweeperTests {
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private static (CaptureSweeper Sweeper, CaptureSessionManager Manager) New() {
        var opts = HostedCaptureOptions.Defaults();
        var manager = new CaptureSessionManager(opts,
            (key, basePort) => CaptureSessionManagerTests.NewSession(
                key == CaptureSessionManager.LocalKey ? 18080 : basePort));
        var sweeper = new CaptureSweeper(manager, opts, TimeProvider.System,
            NullLogger<CaptureSweeper>.Instance);
        return (sweeper, manager);
    }

    private static async Task<CaptureSession> StartedSession(
        CaptureSessionManager manager, string key, TimeSpan age, TimeSpan sinceLastFlow) {
        var s = manager.GetOrCreate(key);
        await s.StartAsync(CancellationToken.None);
        s.StartedUtc = Now - age;
        s.LastFlowUtc = Now - sinceLastFlow;
        return s;
    }

    [Fact]
    public async Task IdleSession_IsStoppedAndRemoved() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", age: TimeSpan.FromHours(1), sinceLastFlow: TimeSpan.FromMinutes(31));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(manager.Get("user-a"));
    }

    [Fact]
    public async Task CappedSession_IsStopped_EvenWhenActive() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", age: TimeSpan.FromHours(5), sinceLastFlow: TimeSpan.FromMinutes(1));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Stopped, s.State);
        Assert.Null(manager.Get("user-a"));
    }

    [Fact]
    public async Task FreshSession_IsLeftAlone() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", age: TimeSpan.FromMinutes(10), sinceLastFlow: TimeSpan.FromMinutes(5));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Running, s.State);
        Assert.NotNull(manager.Get("user-a"));
        await s.StopAsync();
    }

    [Fact]
    public async Task LocalSession_IsNeverSwept() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, CaptureSessionManager.LocalKey,
            age: TimeSpan.FromDays(2), sinceLastFlow: TimeSpan.FromDays(1));
        await sweeper.SweepOnceAsync(Now);
        Assert.Equal(CaptureState.Running, s.State);
        Assert.NotNull(manager.Get(CaptureSessionManager.LocalKey));
        await s.StopAsync();
    }

    [Fact]
    public async Task StoppedSession_IsReleasedOnceIdle() {
        var (sweeper, manager) = New();
        var s = await StartedSession(manager, "user-a", age: TimeSpan.FromHours(1), sinceLastFlow: TimeSpan.FromMinutes(31));
        await s.StopAsync();
        await sweeper.SweepOnceAsync(Now);
        Assert.Null(manager.Get("user-a"));
    }
}
