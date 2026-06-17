using System.Threading.Channels;

namespace EggIncognito.Capture;

// In-memory broker between the capture proxy and the dashboard browsers.
// The proxy raises flows + connection/error events on its own threads; SSE subscribers are served on
// request threads. So all mutable state is guarded by a lock, and each subscriber gets its own bounded
// Channel of CaptureEnvelope - a slow or stalled browser drops its oldest queued messages instead of
// blocking the proxy.
// One SSE stream carries three message kinds via CaptureEnvelope: "flow", "stats", and "notice".
// Designed to live behind a DI singleton so the controller and the capture loop share one hub.
public sealed class CaptureHub
{
    private const int BufferCap = 500; // recent flows kept for snapshot/reconnect
    private const int SubscriberQueueCap = 256;
    private const string KindFlow = "flow";
    private const string KindStats = "stats";
    private const string KindNotice = "notice";

    private readonly object _gate = new();
    private readonly LinkedList<DashboardFlow> _buffer = new();
    private readonly List<Channel<CaptureEnvelope>> _subscribers = [];
    private long _nextId;

    // stats counters, all under _gate
    private int _activeConnections;
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    // Devices remembered from prior runs, seeded at session start. Shown as offline cards until a
    // matching IP connects, at which point the live device adopts the remembered identity.
    private readonly Dictionary<string, RememberedDevice> _known = new(StringComparer.Ordinal);
    // Raised outside the lock whenever the device set changes, so the owner can persist it.
    public Action? DevicesChanged;
    // Session's most-recently-seen device OS + game version, from the decoded request's rinfo. The flow
    // path sees loopback not the device IP, so these are only surfaced when exactly one device is
    // connected, the common single-phone case.
    private string? _lastOs;
    private string? _lastGameVersion;
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

    // When paused, Publish records nothing and broadcasts no flow - dashboard view only; the proxy
    // keeps tunneling and the endpoint/HAR pipeline is governed separately. Stats/connection events
    // still update so the cert pill and device info stay accurate while paused.
    public bool Paused { get; set; }

    // Stamp Id/Timestamp, buffer, update stats, broadcast. Returns stored flow or null if paused.
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
                var (os, gameVersion) = ParseRInfo(flow.RequestJson);
                if (os is not null) _lastOs = os;
                if (gameVersion is not null) _lastGameVersion = gameVersion;
                // When exactly one device is connected, the rinfo OS/version belongs to it - stamp it on
                // that device so it persists per-device and survives into the remembered store.
                if (_devices.Count == 1 && (os is not null || gameVersion is not null))
                {
                    var only = _devices.Values.First();
                    only.Os = os ?? only.Os;
                    only.GameVersion = gameVersion ?? only.GameVersion;
                }
                var bytes = ApproxBytes(flow);
                _bytesCaptured += bytes;
                if (!string.IsNullOrEmpty(flow.Path))
                    _bytesByEndpoint[flow.Path] = _bytesByEndpoint.GetValueOrDefault(flow.Path) + bytes;

                // First successful auxbrain decrypt means the CA is trusted on the device.
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

    // Record a new connection; fires DeviceConnected toast for a newly-seen device.
    public void RecordConnection(int activeCount, string? ip, string timestamp)
    {
        CaptureEvent? notice = null;
        bool needRdns = false;
        lock (_gate)
        {
            _activeConnections = activeCount;
            // A TCP device connect does not tell us whether the cert is trusted - the TLS handshake has
            // not been attempted yet. Leave cert state as Waiting until a flow actually decrypts, going
            // Trusted, or a decrypt error fires, going Untrusted.
            if (!string.IsNullOrEmpty(ip))
            {
                if (!_devices.TryGetValue(ip, out var dev))
                {
                    // Adopt the remembered identity if we have seen this IP in a prior run.
                    _known.TryGetValue(ip, out var prior);
                    dev = new Device(ip, prior?.FirstSeen ?? timestamp)
                    {
                        Hostname = prior?.Hostname,
                        Os = prior?.Os,
                        GameVersion = prior?.GameVersion,
                        TotalConnections = prior?.TotalConnections ?? 0,
                    };
                    _devices[ip] = dev;
                    notice = new CaptureEvent("deviceConnected", $"Device connected {ip}", timestamp);
                    needRdns = prior?.Hostname is null;
                }
                dev.Online = true;
                dev.Connections++;
                dev.TotalConnections++;
                dev.LastSeen = timestamp;
            }
        }
        if (notice is not null) Broadcast(new CaptureEnvelope(KindNotice, null, null, notice));
        if (needRdns && ip is not null) _ = ResolveHostnameAsync(ip);
        DevicesChanged?.Invoke();
        BroadcastStats();
    }

    public void RecordDisconnection(int activeCount, string timestamp)
    {
        lock (_gate)
        {
            _activeConnections = activeCount;
            // When all connections are gone, every live device is now offline. The forwarder cannot
            // tell us which IP dropped, so a zero count means none remain.
            if (activeCount == 0)
                foreach (var d in _devices.Values) d.Online = false;
        }
        Broadcast(new CaptureEnvelope(KindNotice, null, null,
            new CaptureEvent("deviceDisconnected", "Device disconnected", timestamp)));
        DevicesChanged?.Invoke();
        BroadcastStats();
    }

    // Best-effort reverse-DNS for a device IP, off the hot path. Fills the device's Hostname if it
    // resolves and is not just the IP echoed back. Failures are silently ignored - RDNS on a LAN is
    // unreliable and a missing hostname is fine.
    private async Task ResolveHostnameAsync(string ip)
    {
        string? host = null;
        try
        {
            var entry = await System.Net.Dns.GetHostEntryAsync(ip);
            if (!string.IsNullOrEmpty(entry.HostName) && entry.HostName != ip) host = entry.HostName;
        }
        catch { /* no PTR record or lookup failed - leave hostname null */ }

        if (host is null) return;
        lock (_gate) { if (_devices.TryGetValue(ip, out var dev)) dev.Hostname = host; }
        DevicesChanged?.Invoke();
        BroadcastStats();
    }

    // An auxbrain CONNECT was seen; the device is trying to reach the API. This alone does not prove
    // the cert is untrusted - the decrypt may still succeed. We only record that we saw it; a later
    // decrypt error with no successful flow is what flips the state to Untrusted.
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

    // snapshots

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

        // Attribute the session OS/version only when a single device is connected; otherwise we cannot
        // tell which device it came from, since the flow path sees loopback not the device IP.
        var single = _devices.Count == 1;
        var live = _devices.Values
            .OrderBy(d => d.FirstSeen, StringComparer.Ordinal)
            .Select(d => new DeviceInfo(
                d.Ip, d.Hostname, d.Connections, d.FirstSeen, d.LastSeen,
                d.Os ?? (single ? _lastOs : null),
                d.GameVersion ?? (single ? _lastGameVersion : null),
                Online: d.Online,
                TotalConnections: d.TotalConnections));
        // Remembered devices from prior runs that are not live this session, shown as offline cards.
        var offline = _known.Values
            .Where(k => !_devices.ContainsKey(k.Ip))
            .Select(k => new DeviceInfo(
                k.Ip, k.Hostname, 0, k.FirstSeen, k.LastSeen, k.Os, k.GameVersion,
                Online: false, TotalConnections: k.TotalConnections));
        var devices = live.Concat(offline).ToArray();

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
            CertState: _certState.ToString(),
            Running: _proxyRunning,
            Port: _proxyPort);
    }

    // OS + version from rinfo; User-Agent does not carry the OS. Returns (null, null) when absent.
    private static (string? Os, string? GameVersion) ParseRInfo(string? requestJson)
    {
        if (string.IsNullOrEmpty(requestJson)) return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(requestJson);
            if (!doc.RootElement.TryGetProperty("rinfo", out var rinfo)) return (null, null);

            string? os = null;
            if (rinfo.TryGetProperty("platform", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String)
                os = OsLabel(p.GetString());

            string? version = null;
            if (rinfo.TryGetProperty("version", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                version = v.GetString();

            return (os, version);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null);
        }
    }

    // Map Egg, Inc.'s platform enum strings to a human OS name.
    private static string OsLabel(string? platform) => platform?.ToUpperInvariant() switch
    {
        "IOS" => "iOS",
        "DROID" or "ANDROID" => "Android",
        null or "" => "Unknown OS",
        _ => platform!,
    };

    // Mutable per-device record kept in the hub, converted to the immutable DeviceInfo for snapshots.
    private sealed class Device(string ip, string firstSeen)
    {
        public string Ip { get; } = ip;
        public string FirstSeen { get; } = firstSeen;
        public string LastSeen { get; set; } = firstSeen;
        public string? Hostname { get; set; }
        public int Connections { get; set; } // active connections this session
        public int TotalConnections { get; set; } // lifetime, seeded from the remembered value
        public bool Online { get; set; } = true;
        public string? Os { get; set; }
        public string? GameVersion { get; set; }
    }

    // Seed devices remembered from prior runs. Called once at session start, before any connection.
    public void SeedKnownDevices(IReadOnlyList<RememberedDevice> devices)
    {
        lock (_gate)
        {
            _known.Clear();
            foreach (var d in devices) _known[d.Ip] = d;
        }
    }

    // The merged device set, live plus remembered-offline, as a persistable list. Live devices win and
    // carry their up-to-date fields; remembered-only devices are preserved as-is.
    public IReadOnlyList<RememberedDevice> SnapshotRememberedDevices()
    {
        lock (_gate)
        {
            var single = _devices.Count == 1;
            var merged = new Dictionary<string, RememberedDevice>(_known, StringComparer.Ordinal);
            foreach (var d in _devices.Values)
            {
                merged[d.Ip] = new RememberedDevice(
                    d.Ip, d.Hostname,
                    d.Os ?? (single ? _lastOs : merged.GetValueOrDefault(d.Ip)?.Os),
                    d.GameVersion ?? (single ? _lastGameVersion : merged.GetValueOrDefault(d.Ip)?.GameVersion),
                    d.FirstSeen, d.LastSeen, d.TotalConnections);
            }
            return merged.Values.ToList();
        }
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

    // Mark a buffered flow as saved-as-endpoint and re-broadcast it, so the dashboard and any other
    // open tab shows it as saved and a refresh, which replays the buffer, does not re-prompt.
    public void MarkSaved(long id)
    {
        DashboardFlow? updated = null;
        lock (_gate)
        {
            var node = _buffer.First;
            while (node is not null)
            {
                if (node.Value.Id == id) { node.Value = node.Value with { Saved = true }; updated = node.Value; break; }
                node = node.Next;
            }
        }
        if (updated is not null) Broadcast(new CaptureEnvelope(KindFlow, updated, null, null));
    }

    // True if at least one dashboard SSE client is currently connected. Used by the launcher to avoid
    // opening a duplicate browser tab when a page from a prior run is already attached.
    public bool HasSubscribers { get { lock (_gate) return _subscribers.Count > 0; } }

    // Set by CaptureSession on start/stop; pushed on stats so the running pill stays live.
    private bool _proxyRunning;
    private int _proxyPort;
    public void SetProxyState(bool running, int port)
    {
        lock (_gate) { _proxyRunning = running; _proxyPort = port; }
        BroadcastStats();
    }

    // Push a one-off notice to every dashboard (toast + notification center). Used for out-of-band
    // events the proxy itself does not raise, e.g. a failed CA Discord DM at session start.
    public void PostNotice(CaptureEvent notice) =>
        Broadcast(new CaptureEnvelope(KindNotice, null, null, notice));

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
