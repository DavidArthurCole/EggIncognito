using EggIncognito.Bot;
using EggIncognito.Services;
using SyncKit.Contract;

namespace EggIncognito.Tests;

public class NewVersionIngestServiceTests {
    [Fact]
    public async Task ProtoUnchanged_Regens_And_Notifies() {
        var notifier = new FakeNotifier();
        var svc = NewVersionIngestService.ForTest("abc", notifier);
        var evt = new NewVersionEvent { Version = "1.34", ProtoSha = "abc", ApkRef = "n/a" };

        var result = await svc.HandleAsync(evt);

        Assert.Equal(IngestOutcome.Regenerated, result);
        Assert.Contains(notifier.Sent, s => s.Contains("1.34"));
    }

    [Fact]
    public async Task ProtoChanged_Stashes_And_Flags() {
        var notifier = new FakeNotifier();
        var svc = NewVersionIngestService.ForTest("abc", notifier);
        var evt = new NewVersionEvent { Version = "1.35", ProtoSha = "different", ApkRef = "n/a" };

        var result = await svc.HandleAsync(evt);

        Assert.Equal(IngestOutcome.ProtoRefreshNeeded, result);
        Assert.Contains(notifier.Sent, s => s.Contains("refresh") || s.Contains("proto"));
    }

    [Fact]
    public async Task Handle_AlwaysCallsRegistry_BothPaths() {
        int calls = 0;

        static Task NoOp(NewVersionEvent _, CancellationToken __) {
            return Task.CompletedTask;
        }

        var svc = new NewVersionIngestService("expected-sha",
            new FakeNotifier(),
            (_, __) => {
                calls++;
                return Task.CompletedTask;
            },
            NoOp,
            NoOp,
            NoOp);
        await svc.HandleAsync(new NewVersionEvent { Version = "v", ProtoSha = "expected-sha" });
        await svc.HandleAsync(new NewVersionEvent { Version = "v2", ProtoSha = "different" });
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LegacyEvent_PlatformFallsBackToAndroid() {
        string? seenPlatform = null;

        static Task NoOp(NewVersionEvent _, CancellationToken __) {
            return Task.CompletedTask;
        }

        var svc = new NewVersionIngestService("expected-sha",
            new FakeNotifier(),
            (evt, __) => {
                seenPlatform = evt.Platform ?? "android";
                return Task.CompletedTask;
            },
            NoOp,
            NoOp,
            NoOp);

        await svc.HandleAsync(new NewVersionEvent { Version = "1.34", ProtoSha = "expected-sha" });

        Assert.Equal("android", seenPlatform);
    }

    private sealed class FakeNotifier : ISyncNotifier {
        public readonly List<string> Sent = [];

        public Task NotifyAsync(string outcome, CancellationToken ct = default) {
            Sent.Add(outcome);
            return Task.CompletedTask;
        }
    }
}
