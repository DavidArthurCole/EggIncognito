using EggIncognito.Bot;
using EggIncognito.Core.Models;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class NewVersionIngestServiceTests
{
    private sealed class FakeNotifier : ISyncNotifier
    {
        public List<string> Sent = new();
        public Task NotifyAsync(string outcome, CancellationToken ct = default)
        {
            Sent.Add(outcome);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProtoUnchanged_Regens_And_Notifies()
    {
        var notifier = new FakeNotifier();
        var svc = NewVersionIngestService.ForTest(expectedProtoSha: "abc", notifier: notifier);
        var evt = new NewVersionEvent { Version = "1.34", ProtoSha = "abc", ApkRef = "n/a" };

        var result = await svc.HandleAsync(evt);

        Assert.Equal(IngestOutcome.Regenerated, result);
        Assert.Contains(notifier.Sent, s => s.Contains("1.34"));
    }

    [Fact]
    public async Task ProtoChanged_Stashes_And_Flags()
    {
        var notifier = new FakeNotifier();
        var svc = NewVersionIngestService.ForTest(expectedProtoSha: "abc", notifier: notifier);
        var evt = new NewVersionEvent { Version = "1.35", ProtoSha = "different", ApkRef = "n/a" };

        var result = await svc.HandleAsync(evt);

        Assert.Equal(IngestOutcome.ProtoRefreshNeeded, result);
        Assert.Contains(notifier.Sent, s => s.Contains("refresh") || s.Contains("proto"));
    }

    [Fact]
    public async Task Handle_AlwaysCallsRegistry_BothPaths()
    {
        var calls = 0;
        static Task NoOp(NewVersionEvent _, CancellationToken __) => Task.CompletedTask;
        var svc = new NewVersionIngestService("expected-sha",
            new FakeNotifier(),
            registry: (_, __) => { calls++; return Task.CompletedTask; },
            fetch: NoOp,
            regen: NoOp,
            stash: NoOp);
        await svc.HandleAsync(new NewVersionEvent { Version = "v", ProtoSha = "expected-sha" });
        await svc.HandleAsync(new NewVersionEvent { Version = "v2", ProtoSha = "different" });
        Assert.Equal(2, calls);
    }
}
