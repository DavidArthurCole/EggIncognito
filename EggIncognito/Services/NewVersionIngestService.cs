using EggIncognito.Bot;
using SyncKit.Contract;

namespace EggIncognito.Services;
public enum IngestOutcome
{
   
    Regenerated,
   
    ProtoRefreshNeeded
}


//

public sealed class NewVersionIngestService
{
    private readonly string _expectedProtoSha;
    private readonly ISyncNotifier _notifier;
    private readonly Func<NewVersionEvent, CancellationToken, Task> _registry;
    private readonly Func<NewVersionEvent, CancellationToken, Task> _fetch;
    private readonly Func<NewVersionEvent, CancellationToken, Task> _regen;
    private readonly Func<NewVersionEvent, CancellationToken, Task> _stash;

    public NewVersionIngestService(
        string expectedProtoSha,
        ISyncNotifier notifier,
        Func<NewVersionEvent, CancellationToken, Task> registry,
        Func<NewVersionEvent, CancellationToken, Task> fetch,
        Func<NewVersionEvent, CancellationToken, Task> regen,
        Func<NewVersionEvent, CancellationToken, Task> stash)
    {
        _expectedProtoSha = expectedProtoSha;
        _notifier = notifier;
        _registry = registry;
        _fetch = fetch;
        _regen = regen;
        _stash = stash;
    }

    public static NewVersionIngestService ForTest(string expectedProtoSha, ISyncNotifier notifier)
    {
        static Task NoOp(NewVersionEvent _, CancellationToken __) => Task.CompletedTask;
        return new NewVersionIngestService(expectedProtoSha, notifier, NoOp, NoOp, NoOp, NoOp);
    }

    public async Task<IngestOutcome> HandleAsync(NewVersionEvent evt, CancellationToken ct = default)
    {
        await _registry(evt, ct);

        if (!string.Equals(evt.ProtoSha, _expectedProtoSha, StringComparison.OrdinalIgnoreCase))
        {
            await _stash(evt, ct);
            await _notifier.NotifyAsync($"proto changed for {evt.Version}, refresh needed", ct);
            return IngestOutcome.ProtoRefreshNeeded;
        }

        await _fetch(evt, ct);
        await _regen(evt, ct);
        await _notifier.NotifyAsync($"regen staged for {evt.Version}", ct);
        return IngestOutcome.Regenerated;
    }
}
