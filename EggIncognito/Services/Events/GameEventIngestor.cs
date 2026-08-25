using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Events;

public sealed class GameEventIngestor(EggIncognitoDbContext db) {
    private const long IngestLockKey = 872634001;

    public async Task<GameEventIngestResult> IngestAsync(
        IReadOnlyList<GameEventObservation> observations, CancellationToken ct = default) {
        if (observations.Count == 0) return new GameEventIngestResult(0, 0);
        int inserted = 0, updated = 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({IngestLockKey})", ct);
        foreach (var obs in observations) {
            ct.ThrowIfCancellationRequested();
            var local = db.GameEvents.Local.FirstOrDefault(e => GameEventMerge.SameOccurrence(e, obs));
            var lo = obs.Start - GameEventMerge.Window;
            var hi = obs.Start + GameEventMerge.Window;
            var match = local ?? await db.GameEvents
                .Where(e => e.EventId == obs.EventId && e.StartTime >= lo && e.StartTime <= hi)
                .OrderBy(e => e.StartTime)
                .FirstOrDefaultAsync(ct);
            if (match is null) {
                db.GameEvents.Add(GameEventMerge.Create(obs));
                inserted++;
            } else if (GameEventMerge.Apply(match, obs)) {
                updated++;
            }
        }
        if (inserted > 0 || updated > 0) await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new GameEventIngestResult(inserted, updated);
    }
}
