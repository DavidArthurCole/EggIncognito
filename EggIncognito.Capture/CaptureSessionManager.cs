namespace EggIncognito.Capture;

// Thrown when creating a hosted session would exceed MaxConcurrentSessions.
public sealed class CaptureCapacityException()
    : Exception("capture session capacity reached");

// Per-user capture sessions. Local mode uses the single anonymous key so behavior is identical to
// the old singleton. Hosted keys by Discord id. The port pool hands each session a private loopback
// base port; Unobtanium derives +1/+2 internally. The factory receives (key, basePort) so the local
// key keeps its configured port and hosted keys get per-user paths.
public sealed class CaptureSessionManager(HostedCaptureOptions opts, Func<string, int, CaptureSession> factory)
{
    public const string LocalKey = "__local";
    private readonly Dictionary<string, CaptureSession> _sessions = new();
    private readonly object _lock = new();

    public CaptureSession GetOrCreate(string key)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(key, out var s)) return s;
            if (_sessions.Count >= opts.MaxConcurrentSessions && key != LocalKey)
                throw new CaptureCapacityException();
            var basePort = NextFreeBasePort();
            var session = factory(key, basePort);
            _sessions[key] = session;
            return session;
        }
    }

    public CaptureSession? Get(string key) { lock (_lock) return _sessions.GetValueOrDefault(key); }

    public IReadOnlyList<(string Key, CaptureSession Session)> All()
    {
        lock (_lock) return _sessions.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public void Remove(string key) { lock (_lock) _sessions.Remove(key); }

    private int NextFreeBasePort()
    {
        var used = _sessions.Values.Select(s => s.Port).ToHashSet();
        for (var p = opts.PortPoolBase; ; p += 3)
            if (!used.Contains(p)) return p;
    }
}
