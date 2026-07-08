using System.Collections.Concurrent;

namespace EggIncognito.Services.Metrics;

// Lightweight in-process API-rate metrics: a 60-slot ring of per-minute buckets (last hour). Each request
// increments the current minute's total + its 429 count if rate-limited. Singleton; thread-safe via
// Interlocked on the bucket fields. Resets on restart.
public sealed class ApiMetrics(TimeProvider time)
{
    public const int Minutes = 60;

    private sealed class Bucket
    {
        public long Epoch; // minute index this bucket represents
        public int Total;
        public int Limited; // 429s
    }

    private readonly Bucket[] _ring = Enumerable.Range(0, Minutes).Select(_ => new Bucket()).ToArray();

    private long NowMinute() => time.GetUtcNow().ToUnixTimeSeconds() / 60;

    public void Record(bool limited)
    {
        var minute = NowMinute();
        var b = _ring[(int)(minute % Minutes)];
        if (Interlocked.Read(ref b.Epoch) != minute)
        {
            lock (b)
            {
                if (b.Epoch != minute) { b.Epoch = minute; b.Total = 0; b.Limited = 0; }
            }
        }
        Interlocked.Increment(ref b.Total);
        if (limited) Interlocked.Increment(ref b.Limited);
    }

    // The last `Minutes` buckets oldest-first, zero-filled for minutes with no traffic.
    public IReadOnlyList<Point> Snapshot()
    {
        var now = NowMinute();
        var pts = new List<Point>(Minutes);
        for (var i = Minutes - 1; i >= 0; i--)
        {
            var minute = now - i;
            var b = _ring[(int)(minute % Minutes)];
            var (total, limited) = Volatile.Read(ref b.Epoch) == minute ? (b.Total, b.Limited) : (0, 0);
            pts.Add(new Point(DateTimeOffset.FromUnixTimeSeconds(minute * 60), total, limited));
        }
        return pts;
    }

    public sealed record Point(DateTimeOffset Minute, int Total, int Limited);
}
