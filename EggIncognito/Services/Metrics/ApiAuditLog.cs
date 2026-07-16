using System.Collections.Concurrent;

namespace EggIncognito.Services.Metrics;

public sealed class ApiAuditLog(TimeProvider time)
{
    public const int RecentCapacity = 512;
    private const int MaxKeys = 2000;

    public readonly record struct AuditEntry(
        DateTimeOffset Ts, string Method, string Path, int Status, RequestBucket Bucket, string Ip, string? User);

    public sealed class PathRollup
    {
        public long Total;
        public long Internal;
        public long Cross;
        public long External;
        public long LastSeenTicks;
    }

    public sealed class IpRollup
    {
        public long Total;
        public long LastSeenTicks;

        public readonly ConcurrentDictionary<string, byte> Paths = new();
    }

    private readonly AuditEntry[] _ring = new AuditEntry[RecentCapacity];
    private long _writeIndex;
    private readonly object _ringLock = new();

    private readonly ConcurrentDictionary<string, PathRollup> _paths = new();
    private readonly ConcurrentDictionary<string, IpRollup> _ips = new();
    private readonly long[] _bucketTotals = new long[3];
    private long _keysCapped;

    public void Record(string method, string path, int status, RequestBucket bucket, string ip, string? user)
    {
        var now = time.GetUtcNow();
        var entry = new AuditEntry(now, method, path, status, bucket, ip, user);

        lock (_ringLock)
        {
            _ring[_writeIndex % RecentCapacity] = entry;
            _writeIndex++;
        }

        Interlocked.Increment(ref _bucketTotals[(int)bucket]);

        var pr = GetOrCap(_paths, path);
        if (pr is not null)
        {
            Interlocked.Increment(ref pr.Total);
            switch (bucket)
            {
                case RequestBucket.Internal: Interlocked.Increment(ref pr.Internal); break;
                case RequestBucket.Cross: Interlocked.Increment(ref pr.Cross); break;
                default: Interlocked.Increment(ref pr.External); break;
            }
            Volatile.Write(ref pr.LastSeenTicks, now.UtcTicks);
        }

        var ir = GetOrCap(_ips, ip);
        if (ir is not null)
        {
            Interlocked.Increment(ref ir.Total);
            Volatile.Write(ref ir.LastSeenTicks, now.UtcTicks);
            if (ir.Paths.Count < MaxKeys) ir.Paths.TryAdd(path, 0);
        }
    }

    private T? GetOrCap<T>(ConcurrentDictionary<string, T> dict, string key) where T : class, new()
    {
        if (dict.TryGetValue(key, out var v)) return v;
        if (dict.Count >= MaxKeys) { Interlocked.Increment(ref _keysCapped); return null; }
        return dict.GetOrAdd(key, _ => new T());
    }

    public IReadOnlyList<AuditEntry> Recent(int take)
    {
        take = Math.Clamp(take, 1, RecentCapacity);
        var outList = new List<AuditEntry>(take);
        lock (_ringLock)
        {
            var count = (int)Math.Min(_writeIndex, RecentCapacity);
            for (var i = 0; i < count && outList.Count < take; i++)
            {
                var idx = (_writeIndex - 1 - i) % RecentCapacity;
                if (idx < 0) idx += RecentCapacity;
                outList.Add(_ring[idx]);
            }
        }
        return outList;
    }

    public IReadOnlyList<(string Path, PathRollup Roll)> Paths() =>
        _paths.Select(kv => (kv.Key, kv.Value))
              .OrderByDescending(x => Volatile.Read(ref x.Value.Total))
              .ToList();

    public IReadOnlyList<(string Ip, long Total, int DistinctPaths, DateTimeOffset LastSeen)> Ips() =>
        _ips.Select(kv => (
                kv.Key,
                Volatile.Read(ref kv.Value.Total),
                kv.Value.Paths.Count,
                new DateTimeOffset(Volatile.Read(ref kv.Value.LastSeenTicks), TimeSpan.Zero)))
            .OrderByDescending(x => x.Item2)
            .ToList();

    public (long Internal, long Cross, long External) Buckets() =>
        (Interlocked.Read(ref _bucketTotals[0]),
         Interlocked.Read(ref _bucketTotals[1]),
         Interlocked.Read(ref _bucketTotals[2]));

    public long KeysCapped => Interlocked.Read(ref _keysCapped);
}
