using EggIncognito.Data.Services;
using EggIncognito.Models.Events;
using EggIncognito.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Predictions;

public readonly record struct EventOccurrence(double Start, double End);

public sealed record EventStreamStats(
    string Type,
    bool Ultra,
    double LastStart,
    double MedianIntervalSeconds,
    double DurationSeconds,
    double WindowSeconds,
    int Samples);

public sealed class EventPredictor(EggIncognitoDbContext db, EventDataVersion version, EventPredictionCache cache) {
    public async Task<EventPredictionSet> GetAsync(CancellationToken ct = default) {
        var stats = await GetStatsAsync(ct);
        double now = UnixSeconds.FromTime(DateTimeOffset.UtcNow);
        var predictions = stats
            .Select(s => Project(s, now))
            .OrderBy(p => p.PredictedStart)
            .ToList();
        return new EventPredictionSet(now, predictions);
    }

    private async Task<IReadOnlyList<EventStreamStats>> GetStatsAsync(CancellationToken ct) {
        long v = version.Version;
        if (cache.Version == v && cache.Value is { } cached) return cached;

        var rows = await db.GameEvents.AsNoTracking()
            .OrderBy(e => e.StartTime)
            .Select(e => new { e.EventType, e.Ultra, e.StartTime, e.EndTime })
            .ToListAsync(ct);
        var stats = rows
            .GroupBy(r => (r.EventType, r.Ultra))
            .Select(g => StatsFor(
                g.Key.EventType, g.Key.Ultra,
                g.Select(r => new EventOccurrence(
                    UnixSeconds.FromTime(r.StartTime), UnixSeconds.FromTime(r.EndTime))).ToList()))
            .OfType<EventStreamStats>()
            .ToList();

        lock (cache) {
            cache.Value = stats;
            cache.Version = v;
        }
        return stats;
    }

    public static EventStreamStats? StatsFor(
        string type, bool ultra, IReadOnlyList<EventOccurrence> occurrences) {
        var starts = occurrences.Select(o => o.Start).Distinct().OrderBy(s => s).ToList();
        if (starts.Count < 4) return null;

        var intervals = new List<double>(starts.Count - 1);
        for (int i = 1; i < starts.Count; i++) intervals.Add(starts[i] - starts[i - 1]);
        double median = RobustStats.Median(intervals);
        double spread = RobustStats.Mad(intervals, median);
        if (IsUnstable(median, spread)) return null;

        double duration = RobustStats.Median(occurrences.Select(o => o.End - o.Start).ToList());
        return new EventStreamStats(type, ultra, starts[^1], median, duration, spread, starts.Count);
    }

    public static EventPrediction Project(EventStreamStats stats, double now) {
        double predictedStart = stats.LastStart + stats.MedianIntervalSeconds;
        while (stats.MedianIntervalSeconds > 0 && predictedStart < now) predictedStart += stats.MedianIntervalSeconds;
        return new EventPrediction(
            stats.Type, stats.Ultra, stats.LastStart, stats.MedianIntervalSeconds,
            predictedStart, predictedStart + stats.DurationSeconds, stats.WindowSeconds, stats.Samples);
    }

    public static EventPrediction? PredictGroup(
        string type, bool ultra, IReadOnlyList<EventOccurrence> occurrences, double now) =>
        StatsFor(type, ultra, occurrences) is { } stats ? Project(stats, now) : null;

    internal static bool IsUnstable(double median, double spread) => median <= 0 || spread > median;
}
