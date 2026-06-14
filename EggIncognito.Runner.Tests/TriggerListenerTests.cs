using EggIncognito.Runner.Runners;
using EggIncognito.Runner.Trigger;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class TriggerListenerTests
{
    [Fact]
    public void Handle_BadBearer_Is401()
    {
        var h = new ResyncHandler("secret", _ => new RunOutcome(true, "1", "sha", "ok"));
        var r = h.Handle("Bearer wrong", force: true);
        Assert.Equal(401, r.Status);
    }

    [Fact]
    public void Handle_GoodBearer_RunsAndReturns200()
    {
        var h = new ResyncHandler("secret", force => new RunOutcome(true, "111343", "sha", "emitted"));
        var r = h.Handle("Bearer secret", force: true);
        Assert.Equal(200, r.Status);
        Assert.Equal("111343", r.Outcome!.Build);
    }

    [Fact]
    public void Handle_Concurrent_Is409()
    {
        var gate = new ManualResetEventSlim(false);
        var started = new ManualResetEventSlim(false);
        var h = new ResyncHandler("secret", force => { started.Set(); gate.Wait(); return new RunOutcome(true, "1", "s", "ok"); });
        var t = Task.Run(() => h.Handle("Bearer secret", force: true));
        started.Wait();
        var second = h.Handle("Bearer secret", force: true);
        Assert.Equal(409, second.Status);
        gate.Set();
        t.Wait();
    }
}
