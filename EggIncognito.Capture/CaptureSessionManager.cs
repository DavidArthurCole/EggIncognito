namespace EggIncognito.Capture;

public sealed class CaptureCapacityException()
    : Exception("capture session capacity reached");

public sealed class CaptureSessionManager(HostedCaptureOptions opts, Func<string, int, CaptureSession> factory) {
    public const string LocalKey = "__local";
    private readonly Lock _lock = new();
    private readonly Dictionary<string, CaptureSession> _sessions = [];

    public CaptureSession GetOrCreate(string key) {
        lock (_lock) {
            if (_sessions.TryGetValue(key, out var s)) return s;
            if (_sessions.Count >= opts.MaxConcurrentSessions && key != LocalKey)
                throw new CaptureCapacityException();
            int basePort = NextFreeBasePort();
            var session = factory(key, basePort);
            _sessions[key] = session;
            return session;
        }
    }

    public CaptureSession? Get(string key) {
        lock (_lock) return _sessions.GetValueOrDefault(key);
    }

    public IReadOnlyList<(string Key, CaptureSession Session)> All() {
        lock (_lock) return _sessions.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public void Remove(string key) {
        lock (_lock) _sessions.Remove(key);
    }

    private int NextFreeBasePort() {
        var used = _sessions.Values.Select(s => s.Port).ToHashSet();
        for (int p = opts.PortPoolBase; ; p += 3) {
            if (!used.Contains(p))
                return p;
        }
    }
}
