using EggIncognito.Runner.Runners;
using EggIncognito.Runner.Trigger;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class DeviceResyncHandlerTests {
    private sealed class FakeRunner(string platform, Func<bool, RunOutcome> run) : IDeviceRunner {
        public string Platform => platform;
        public RunOutcome RunOnce(bool force) => run(force);
    }

    private static DeviceResyncHandler Handler(params (string id, Func<bool, RunOutcome> run)[] runners) {
        var dict = new Dictionary<string, IDeviceRunner>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, run) in runners) dict[id] = new FakeRunner("android", run);
        return new DeviceResyncHandler("secret", dict);
    }

    [Fact]
    public void HandleOne_BadBearer_Is401() {
        var h = Handler(("pixel", _ => new RunOutcome(true, "1", "s", "ok")));
        Assert.Equal(401, h.HandleOne("Bearer wrong", "pixel", true).Status);
    }

    [Fact]
    public void HandleOne_UnknownId_Is404() {
        var h = Handler(("pixel", _ => new RunOutcome(true, "1", "s", "ok")));
        Assert.Equal(404, h.HandleOne("Bearer secret", "ghost", true).Status);
    }

    [Fact]
    public void HandleOne_GoodBearer_Runs200() {
        var h = Handler(("pixel", _ => new RunOutcome(true, "111343", "s", "emitted")));
        var r = h.HandleOne("Bearer secret", "pixel", true);
        Assert.Equal(200, r.Status);
        Assert.Equal("111343", r.Outcome!.Build);
    }

    [Fact]
    public async Task HandleOne_ConcurrentSameDevice_Is409() {
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var h = Handler(("pixel", _ => { started.Set(); gate.Wait(); return new RunOutcome(true, "1", "s", "ok"); }));
        var t = Task.Run(() => h.HandleOne("Bearer secret", "pixel", true));
        started.Wait();
        var second = h.HandleOne("Bearer secret", "pixel", true);
        Assert.Equal(409, second.Status);
        gate.Set();
        await t;
    }

    [Fact]
    public void HandleAll_RunsEveryDevice() {
        var h = Handler(
            ("pixel", _ => new RunOutcome(true, "a", "s", "emitted")),
            ("iphone", _ => new RunOutcome(false, "b", null, "no change")));
        var results = h.HandleAll("Bearer secret", true);
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(200, r.Status));
    }

    [Fact]
    public void HandleAll_BadBearer_Is401() {
        var h = Handler(("pixel", _ => new RunOutcome(true, "1", "s", "ok")));
        var results = h.HandleAll("Bearer wrong", true);
        Assert.Single(results);
        Assert.Equal(401, results[0].Status);
    }
}
