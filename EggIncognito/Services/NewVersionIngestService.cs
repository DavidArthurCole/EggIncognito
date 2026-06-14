using EggIncognito.Bot;
using EggIncognito.Core.Models;

namespace EggIncognito.Services;

// Outcome of handling one device-farm NewVersionEvent.
public enum IngestOutcome
{
    // proto matched the build: affected endpoints regenerated into staged/ for human review.
    Regenerated,
    // proto differed: artifacts stashed + flagged, no regen. A human must refresh the frozen ei.proto.
    ProtoRefreshNeeded
}

// NewVersionIngestService is the sync handler body. It classifies an inbound event by protoSha
// against the build's frozen ei.proto identity, then either regenerates affected endpoints into
// staged/ (proto unchanged, the common path) or stashes the artifacts and flags a manual proto
// refresh (proto changed). It never edits ei.proto and never writes default/.
//
// Side effects run behind delegate seams (fetch, regen, stash) so production drives EndpointExtractor
// + the ApkFetchRoot while tests bypass disk and Discord entirely. The notifier is the same
// ISyncNotifier seam in both paths.
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

    // ForTest builds an instance with no-op registry/fetch/regen/stash seams, for branch-level unit
    // tests that exercise the classify-and-notify logic without disk artifacts or a live bot.
    public static NewVersionIngestService ForTest(string expectedProtoSha, ISyncNotifier notifier)
    {
        static Task NoOp(NewVersionEvent _, CancellationToken __) => Task.CompletedTask;
        return new NewVersionIngestService(expectedProtoSha, notifier, NoOp, NoOp, NoOp, NoOp);
    }

    public async Task<IngestOutcome> HandleAsync(NewVersionEvent evt, CancellationToken ct = default)
    {
        // Registry capture runs on every build the farm sees, independent of the regen/refresh split.
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
