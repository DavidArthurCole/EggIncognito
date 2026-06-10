using EggIncognito.Bot;
using EggIncognito.Models;
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
}
