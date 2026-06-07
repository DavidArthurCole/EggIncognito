using System.Threading.Channels;

namespace EggIncognito.Capture;

// In-memory broker between the capture proxy and the dashboard browser(s).
//
// The proxy raises flows + connection/error events on its own threads; SSE subscribers are
// served on request threads. So all mutable state (flow buffer, stats counters, subscriber set)
// is guarded by a lock, and each subscriber gets its own bounded Channel of CaptureEnvelope - a
// slow or stalled browser drops its oldest queued messages (DropOldest) instead of blocking the
// proxy.
//
// One SSE stream carries three message kinds via CaptureEnvelope: "flow" (a captured flow),
// "stats" (a stats snapshot), and "notice" (a toast event). Designed to live behind a DI
// singleton so the controller and the capture loop share one hub.
public sealed class CaptureHub
{
    private const int BufferCap = 500; // recent flows kept for snapshot / reconnect
    private const int SubscriberQueueCap = 256;
    private const string KindFlow = "flow";
    private const string KindStats = "stats";
    private const string KindNotice = "notice";

    private readonly object _gate = new();
    private readonly LinkedList<DashboardFlow> _buffer = new();
    private readonly List<Channel<CaptureEnvelope>> _subscribers = [];
    private long _nextId;

    // --- stats counters (all under _gate) ---
    private int _activeConnections;
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    // Session's most-recently-seen decrypted User-Agent. The flow path sees only loopback, so a UA
    // cannot be reliably tied to a specific device IP - we keep the latest and only surface it when
    // exactly one device is connected (the overwhelmingly common single-phone case).
    private string? _lastUserAgent;
    private int _capturedAuxbrain;
    private int _passthrough;
    private readonly HashSet<string> _endpoints = new(StringComparer.Ordinal);
    private int _decryptOk;
    private int _decryptErrors;
    private string? _lastError;
    private long _bytesCaptured;
    private readonly Dictionary<string, long> _bytesByEndpoint = new(StringComparer.Ordinal);
    private bool _sawAuxbrainConnect;
    private CertState _certState = CertState.Waiting;

    // When paused, Publish records nothing and broadcasts no flow (dashboard view only - the
    // proxy keeps tunneling and the endpoint/HAR pipeline is governed separately). Stats/connection
    // events still update so the cert pill and device info stay accurate while paused.
    public bool Paused { get; set; }

    // --- flows ---------------------------------------------------------------

    // Assign an id, stamp a timestamp, buffer it, update stats, and broadcast. Returns the stored
    // flow (with Id/Timestamp filled), or null if paused. The caller passes a flow whose Id and
    // Timestamp are placeholders; the hub owns those. isAuxbrain marks a decrypted auxbrain flow
    // (counts toward capture stats + flips the cert state to Trusted).
    public DashboardFlow? Publish(DashboardFlow flow, string timestamp, bool isAuxbrain = true)
    {
        DashboardFlow? stored = null;
        Channel<CaptureEnvelope>[] targets;
        CaptureEvent? trustNotice = null;

        lock (_gate)
        {
            if (isAuxbrain)
            {
                _capturedAuxbrain++;
                _decryptOk++;
                if (!string.IsNullOrEmpty(flow.Path)) _endpoints.Add(flow.Path);
                var ua = FindUserAgent(flow.RequestHeadersRaw) ?? FindUserAgent(flow.RequestHeaders);
                if (ua is not null) _lastUserAgent = ua;
                var bytes = ApproxBytes(flow);
                _bytesCaptured += bytes;
                if (!string.IsNullOrEmpty(flow.Path))
                    _bytesByEndpoint[flow.Path] = _bytesByEndpoint.GetValueOrDefault(flow.Path) + bytes;

                // First successful auxbrain decrypt => the CA is trusted on the device.
                if (_certState != CertState.Trusted)
                {
                    _certState = CertState.Trusted;
                    trustNotice = new CaptureEvent("certTrusted", "Cert trusted - capturing!", timestamp);
                }
            }
            else
            {
                _passthrough++;
            }

            if (!Paused && isAuxbrain)
            {
                stored = flow with { Id = ++_nextId, Timestamp = timestamp };
                _buffer.AddLast(stored);
                while (_buffer.Count > BufferCap) _buffer.RemoveFirst();
            }

            targets = _subscribers.ToArray();
        }

        foreach (var ch in targets)
        {
            if (stored is not null) ch.Writer.TryWrite(new CaptureEnvelope(KindFlow, stored, null, null));
            if (trustNotice is not null) ch.Writer.TryWrite(new CaptureEnvelope(KindNotice, null, null, trustNotice));
        }
        if (trustNotice is not null) BroadcastStats();

        return stored;
    }

    // --- connection + decrypt events (from the proxy) ------------------------

    // A client connected. activeCount is the proxy's current active connection count; ip is the
    // remote address if known. Emits a deviceConnected toast for a newly-seen device.
    public void RecordConnection(int activeCount, string? ip, string timestamp)
    {
        CaptureEvent? notice = null;
        bool needRdns = false;
        lock (_gate)
        {
            _activeConnections = activeCount;
            // A device connecting (TCP) does NOT tell us whether the cert is trusted - the TLS
            // handshake has not been attempted yet. Leave cert state as-is (Waiting) until a flow
            // actually decrypts (-> Trusted) or a decrypt error fires (-> Untrusted).
            if (!string.IsNullOrEmpty(ip))
            {
                if (!_devices.TryGetValue(ip, out var dev))
                {
                    dev = new Device(ip, timestamp);
                    _devices[ip] = dev;
                    notice = new CaptureEvent("deviceConnected", $"Device connected {ip}", timestamp);
                    needRdns = true;
                }
                dev.Connections++;
                dev.LastSeen = timestamp;
            }
        }
        if (notice is not null) Broadcast(new CaptureEnvelope(KindNotice, null, null, notice));
        if (needRdns && ip is not null) _ = ResolveHostnameAsync(ip);
        BroadcastStats();
    }

    public void RecordDisconnection(int activeCount, string timestamp)
    {
        lock (_gate) _activeConnections = activeCount;
        Broadcast(new CaptureEnvelope(KindNotice, null, null,
            new CaptureEvent("deviceDisconnected", "Device disconnected", timestamp)));
        BroadcastStats();
    }

    // Best-effort reverse-DNS for a device IP, off the hot path. Fills the device's Hostname if it
    // resolves (and is not just the IP echoed back). Failures are silently ignored - RDNS on a LAN
    // is unreliable and a missing hostname is fine.
    private async Task ResolveHostnameAsync(string ip)
    {
        string? host = null;
        try
        {
            var entry = await System.Net.Dns.GetHostEntryAsync(ip);
            if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName != ip) host = entry.HostName;
        }
        catch { /* no PTR record / lookup failed - leave hostname null */ }

        if (host is null) return;
        lock (_gate) { if (_devices.TryGetValue(ip, out var dev)) dev.Hostname = host; }
        BroadcastStats();
    }

    // An auxbrain CONNECT was seen (the device is trying to reach the API). This alone does NOT
    // prove the cert is untrusted - the decrypt may still succeed. We only record that we saw it;
    // a later decrypt error (with no successful flow) is what flips the state to Untrusted.
    public void RecordAuxbrainConnect()
    {
        lock (_gate) _sawAuxbrainConnect = true;
        BroadcastStats();
    }

    // A decrypt/TLS error fired. Strong signal the CA is not trusted on the device.
    public void RecordDecryptError(string message, string timestamp)
    {
        lock (_gate)
        {
            _decryptErrors++;
            _lastError = message;
            // Only downgrade to Untrusted if we have not already proven trust this session.
            if (_certState != CertState.Trusted && (_sawAuxbrainConnect || _activeConnections > 0))
                _certState = CertState.Untrusted;
        }
        Broadcast(new CaptureEnvelope(KindNotice, null, null,
            new CaptureEvent("decryptError", "Decrypt error: " + message, timestamp)));
        BroadcastStats();
    }

    // --- snapshots -----------------------------------------------------------

    public IReadOnlyList<DashboardFlow> Snapshot()
    {
        lock (_gate) return _buffer.ToArray();
    }

    public CaptureStats StatsSnapshot()
    {
        lock (_gate) return BuildStats();
    }

    private CaptureStats BuildStats()
    {
        string? biggest = null;
        long biggestBytes = 0;
        foreach (var (k, v) in _bytesByEndpoint)
            if (v > biggestBytes) { biggest = k; biggestBytes = v; }

        // Attribute the session UA only when a single device is connected (otherwise we cannot tell
        // which device it came from - the flow path sees loopback, not the device IP).
        var single = _devices.Count == 1;
        var devices = _devices.Values
            .OrderBy(d => d.FirstSeen, StringComparer.Ordinal)
            .Select(d => new DeviceInfo(
                d.Ip, d.Hostname, d.Connections, d.FirstSeen, d.LastSeen,
                single ? _lastUserAgent : null))
            .ToArray();

        return new CaptureStats(
            ActiveConnections: _activeConnections,
            DeviceCount: _devices.Count,
            Devices: devices,
            CapturedAuxbrain: _capturedAuxbrain,
            Passthrough: _passthrough,
            UniqueEndpoints: _endpoints.Count,
            DecryptOk: _decryptOk,
            DecryptErrors: _decryptErrors,
            LastError: _lastError,
            BytesCaptured: _bytesCaptured,
            BiggestEndpoint: biggest,
            BiggestEndpointBytes: biggestBytes,
            CertState: _certState.ToString());
    }

    // Pull the User-Agent value out of a header list (case-insensitive name match).
    private static string? FindUserAgent(IReadOnlyList<DashboardHeader>? headers)
    {
        if (headers is null) return null;
        foreach (var h in headers)
            if (string.Equals(h.Name, "User-Agent", StringComparison.OrdinalIgnoreCase))
                return h.Value;
        return null;
    }

    // Mutable per-device record kept in the hub (converted to the immutable DeviceInfo for snapshots).
    private sealed class Device(string ip, string firstSeen)
    {
        public string Ip { get; } = ip;
        public string FirstSeen { get; } = firstSeen;
        public string LastSeen { get; set; } = firstSeen;
        public string? Hostname { get; set; }
        public int Connections { get; set; }
    }

    public void Clear()
    {
        lock (_gate) _buffer.Clear();
    }

    public DashboardFlow? Find(long id)
    {
        lock (_gate)
        {
            foreach (var f in _buffer)
                if (f.Id == id) return f;
            return null;
        }
    }

    // --- subscriptions + broadcast ------------------------------------------

    public (ChannelReader<CaptureEnvelope> Reader, IDisposable Subscription) Subscribe()
    {
        var ch = Channel.CreateBounded<CaptureEnvelope>(new BoundedChannelOptions(SubscriberQueueCap)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        lock (_gate) _subscribers.Add(ch);
        return (ch.Reader, new Subscription(this, ch));
    }

    private void Broadcast(CaptureEnvelope env)
    {
        Channel<CaptureEnvelope>[] targets;
        lock (_gate) targets = _subscribers.ToArray();
        foreach (var ch in targets) ch.Writer.TryWrite(env);
    }

    private void BroadcastStats()
    {
        CaptureEnvelope env;
        Channel<CaptureEnvelope>[] targets;
        lock (_gate)
        {
            env = new CaptureEnvelope(KindStats, null, BuildStats(), null);
            targets = _subscribers.ToArray();
        }
        foreach (var ch in targets) ch.Writer.TryWrite(env);
    }

    private void Unsubscribe(Channel<CaptureEnvelope> ch)
    {
        lock (_gate) _subscribers.Remove(ch);
        ch.Writer.TryComplete();
    }

    // Rough on-the-wire size of a flow for the bytes stat: base64 lengths of request + response.
    private static long ApproxBytes(DashboardFlow flow) =>
        (flow.ResponseB64?.Length ?? 0) + (flow.RequestDataB64?.Length ?? 0);

    private sealed class Subscription(CaptureHub hub, Channel<CaptureEnvelope> ch) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            hub.Unsubscribe(ch);
        }
    }
}
