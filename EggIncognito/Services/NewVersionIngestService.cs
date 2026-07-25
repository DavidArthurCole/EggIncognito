using EggIdentity.Contract;
using EggIncognito.Bot;

namespace EggIncognito.Services;

public enum IngestOutcome {
    Regenerated,

    ProtoRefreshNeeded
}

//

public sealed class NewVersionIngestService(
    string expectedProtoSha,
    ISyncNotifier notifier,
    Func<NewVersionEvent, CancellationToken, Task> registry,
    Func<NewVersionEvent, CancellationToken, Task> fetch,
    Func<NewVersionEvent, CancellationToken, Task> regen,
    Func<NewVersionEvent, CancellationToken, Task> stash) {
    public static NewVersionIngestService ForTest(string expectedProtoSha, ISyncNotifier notifier) {
        static Task NoOp(NewVersionEvent _, CancellationToken __) {
            return Task.CompletedTask;
        }

        return new NewVersionIngestService(expectedProtoSha, notifier, NoOp, NoOp, NoOp, NoOp);
    }

    public async Task<IngestOutcome> HandleAsync(NewVersionEvent evt, CancellationToken ct = default) {
        await registry(evt, ct);

        if (!string.Equals(evt.ProtoSha, expectedProtoSha, StringComparison.OrdinalIgnoreCase)) {
            await stash(evt, ct);
            await notifier.NotifyAsync($"proto changed for {evt.Version}, refresh needed", ct);
            return IngestOutcome.ProtoRefreshNeeded;
        }

        await fetch(evt, ct);
        await regen(evt, ct);
        await notifier.NotifyAsync($"regen staged for {evt.Version}", ct);
        return IngestOutcome.Regenerated;
    }
}
