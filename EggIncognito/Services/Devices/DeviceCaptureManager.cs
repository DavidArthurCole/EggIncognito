using System.Collections.Concurrent;
using System.Threading.Channels;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;

namespace EggIncognito.Services.Devices;

// Persistent per-device capture. Each declared device gets its OWN long-lived capture proxy on a dedicated
// loopback+LAN port, so a harvested flow maps to exactly one device (attribution by listener identity). The
// device's system HTTP proxy is pointed at that port (by the probe loop + on start) so its auxbrain traffic
// flows through and its rinfo (build/clientVersion/version) is harvested onto disk (DeviceRinfoStore).
//
// Rolling-rinfo-only: no HAR, no endpoint extract, no flow history. Just decode the request enough to read
// rinfo, store the latest per device. This is the authoritative iOS auxbrain build (the static binary cannot
// give it). Gated by DeviceCapture:Enabled (default off); off => this service no-ops entirely.
//
// Failure isolation: one device's proxy failing to bind/start is logged + recorded, never kills the others
// or the host. Idempotent: EnsureAsync reconciles the live listener set to the declared device set.
public sealed class DeviceCaptureManager(
    DeviceCaptureConfig config,
    DeviceConfig devices,
    string capturePath,
    string caPath,
    Func<bool, ICaptureProxy>? proxyFactory,
    string contentRoot,
    ILogger<DeviceCaptureManager> logger,
    IEnumerable<EggIncognito.Core.Services.Devices.IDeviceCaInstaller>? caInstallers = null) : IHostedService
{
    private readonly Func<bool, ICaptureProxy> _proxyFactory = proxyFactory ?? (verbose => new UnobtaniumCaptureProxy(verbose));
    private readonly ConcurrentDictionary<string, DeviceCapture> _captures = new();
    private readonly ConcurrentDictionary<string, DeviceCaptureDiag> _diag = new();
    private readonly DeviceRinfoStore _rinfo = new(capturePath);
    // CA installer per platform. Rooted/jailbroken devices: the capture CA is installed + trusted on the
    // device automatically (no tap) so the proxy's MITM decrypts. Empty when none registered (tests).
    private readonly IReadOnlyDictionary<string, EggIncognito.Core.Services.Devices.IDeviceCaInstaller> _caInstallers =
        (caInstallers ?? []).GroupBy(c => c.Platform, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    // Per-device listener: its proxy, dedicated port, and the harvest pump.
    private sealed record DeviceCapture(ICaptureProxy Proxy, int Port, Channel<CapturedFlow> Queue, Task Pump);

    public int PortFor(string deviceId) => _captures.TryGetValue(deviceId, out var c) ? c.Port : 0;
    public DeviceRinfoStore Rinfo => _rinfo;

    // Live per-device capture diagnostics. When rinfo never harvests, these tell WHICH boundary fails:
    // clientConnects=0 -> device not routing through the proxy (proxy not applied / app bypassing it);
    // connects>0 but auxbrainConnects=0 -> reaching the proxy but not auxbrain (DNS/host filter);
    // auxbrainConnects>0 but flows=0 -> CA not trusted (TLS handshake fails; lastDecryptError set);
    // flows>0 but rinfoHarvests=0 -> requests decode but carry no rinfo (type/field mismatch).
    public DeviceCaptureDiag DiagFor(string deviceId) =>
        _diag.TryGetValue(deviceId, out var d) ? d.Snapshot() : DeviceCaptureDiag.Empty;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!config.Enabled)
        {
            logger.LogInformation("device capture: disabled (DeviceCapture:Enabled=false)");
            return;
        }
        if (devices.Devices.Count == 0)
        {
            logger.LogInformation("device capture: no devices declared");
            return;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await EnsureAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        foreach (var id in _captures.Keys.ToList()) await TeardownAsync(id);
    }

    // Each device proxy consumes THREE consecutive ports: the LAN-facing port the device points at, plus
    // port+1 (internal loopback proxy) and port+2 (internal TLS-forward) that UnobtaniumCaptureProxy binds.
    // So devices must be spaced >= 3 apart or device N's LAN port collides with device N-1's internal loopback
    // (the bug: BasePort+index spacing of 1 left the iOS LAN port == the Android internal port, so the iOS
    // forwarder never bound and iOS traffic was silently dropped). Stride of 3 gives each device its own block.
    public const int PortsPerDevice = 3;

    // The LAN-facing port for the device at declaration index `i`. Each device owns a 3-port block so the
    // blocks never overlap (see PortsPerDevice). Pure + public so the spacing is unit-tested.
    public static int PortForIndex(int basePort, int index) => basePort + index * PortsPerDevice;

    // Reconcile the live listener set to the declared devices: start one per declared device that has none,
    // assigning a dedicated 3-port block (BasePort + index*3). Safe to call repeatedly.
    public async Task EnsureAsync(CancellationToken ct)
    {
        if (!config.Enabled) return;
        for (var i = 0; i < devices.Devices.Count; i++)
        {
            var d = devices.Devices[i];
            if (_captures.ContainsKey(d.Id)) continue;
            await StartOneAsync(d, PortForIndex(config.BasePort, i), ct);
        }
    }

    private async Task StartOneAsync(DeviceEntry d, int port, CancellationToken ct)
    {
        try
        {
            var queue = Channel.CreateUnbounded<CapturedFlow>(new UnboundedChannelOptions { SingleReader = true });
            var decoder = new FlowDecoder(contentRoot);
            var pump = Task.Run(() => PumpAsync(d.Id, queue, decoder), ct);

            var diag = _diag.GetOrAdd(d.Id, _ => new DeviceCaptureDiag());

            var proxy = _proxyFactory(false);
            proxy.FlowCaptured += flow =>
            {
                diag.Bump(ref diag.Flows);
                queue.Writer.TryWrite(flow);
            };
            // Boundary signals: which stage the device's traffic reaches. Logged + counted so a single
            // game launch after deploy reveals where capture breaks (see DiagFor).
            proxy.ClientConnected += (count, ip) =>
            {
                diag.Bump(ref diag.ClientConnects);
                logger.LogInformation("device capture: {Id} client connected (active={Count}, ip={Ip})", d.Id, count, ip ?? "?");
            };
            proxy.AuxbrainConnect += () =>
            {
                diag.Bump(ref diag.AuxbrainConnects);
                logger.LogInformation("device capture: {Id} auxbrain CONNECT decrypted", d.Id);
            };
            proxy.DecryptError += msg =>
            {
                diag.LastDecryptError = msg;
                logger.LogWarning("device capture: {Id} decrypt error: {Msg}", d.Id, msg);
            };
            // Record every CONNECT target so "not reaching auxbrain" is diagnosable: if www.auxbrain.com
            // never appears here while the phone connects, the game is bypassing the proxy for auxbrain
            // (e.g. QUIC/UDP 443); if it appears with decrypt=false, the host filter is wrong.
            proxy.ConnectSeen += (host, willDecrypt) => diag.NoteConnect(host, willDecrypt);
            await proxy.StartAsync(port, caPath, ct);

            _captures[d.Id] = new DeviceCapture(proxy, port, queue, pump);
            logger.LogInformation("device capture: {Id} listening on :{Port} (CA {Ca}, freshCa={Fresh})",
                d.Id, port, caPath, proxy.FreshCa);

            // A FRESH CA means the persisted root was missing this boot, so every cert already installed on a
            // device is now for the OLD CA and will NOT match. In a container this happens on every recreate
            // unless the captures dir is a persistent volume. Warn loudly: without persistence the per-device
            // CA install is futile (install -> redeploy -> new CA -> stale cert -> "CA untrusted" forever).
            if (proxy.FreshCa)
                logger.LogWarning(
                    "device capture: {Id} FRESH CA minted ({Ca}). Any cert installed on a device last run is now " +
                    "STALE. Persist the captures dir across restarts (mount a volume at the CA's directory) or " +
                    "the device trust resets on every deploy.", d.Id, caPath);

            // Auto-install + trust the capture CA on the (rooted/jailbroken) device, so the proxy's MITM TLS
            // is accepted and flows decrypt. Best-effort: a failure leaves the device untrusted (the chip will
            // show "CA untrusted"), never blocks the listener. Idempotent, so running it every start is safe.
            await InstallCaAsync(d, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "device capture: {Id} failed to start on :{Port}", d.Id, port);
        }
    }

    // Install + trust the capture CA on one device via its platform installer. Best-effort + logged; a miss
    // never breaks capture (the device just stays untrusted until the next attempt). Public so the device
    // status panel / a manual button can re-trigger after a cert change without restarting the host.
    public async Task<(bool Ok, string? Note)> InstallCaAsync(DeviceEntry d, CancellationToken ct)
    {
        if (!_caInstallers.TryGetValue(d.Platform, out var installer))
            return (false, $"no CA installer for {d.Platform}");
        try
        {
            var target = new EggIncognito.Core.Services.Devices.DeviceCaTarget(d.Id, d.Platform, d.Target);
            var (ok, note) = await installer.InstallAsync(target, caPath, ct);
            if (ok) logger.LogInformation("device capture: {Id} CA installed ({Note})", d.Id, note);
            else logger.LogWarning("device capture: {Id} CA install failed: {Note}", d.Id, note);
            return (ok, note);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "device capture: {Id} CA install threw", d.Id);
            return (false, ex.Message);
        }
    }

    // Harvest pump: decode each flow's request enough to read rinfo, persist the latest for this device.
    // A single bad flow must never kill the pump (rolling capture).
    private async Task PumpAsync(string deviceId, Channel<CapturedFlow> queue, FlowDecoder decoder)
    {
        await foreach (var flow in queue.Reader.ReadAllAsync())
        {
            try
            {
                var req = decoder.DecodeRequest(EndpointExtractor.NormalizePath(flow.Url), flow.RequestDataB64);
                var obs = RinfoHarvester.TryHarvest(req.JsonRaw);
                if (obs is not null)
                {
                    _rinfo.Observe(deviceId, obs, DateTimeOffset.UtcNow.ToString("O"));
                    if (_diag.TryGetValue(deviceId, out var dg)) dg.Bump(ref dg.RinfoHarvests);
                }
            }
            catch { /* one bad flow must not break rolling capture */ }
        }
    }

    private async Task TeardownAsync(string id)
    {
        if (!_captures.TryRemove(id, out var c)) return;
        try { await c.Proxy.StopAsync(); } catch { }
        c.Queue.Writer.TryComplete();
        try { await c.Pump; } catch { }
        try { await c.Proxy.DisposeAsync(); } catch { }
        logger.LogInformation("device capture: {Id} torn down", id);
    }
}

// Live per-device capture counters, incremented from proxy event threads + the harvest pump (hence
// Interlocked). A snapshot is exposed read-only so the device status surface can show which capture
// boundary the device's traffic last reached.
public sealed class DeviceCaptureDiag
{
    public long ClientConnects;
    public long AuxbrainConnects;
    public long Flows;
    public long RinfoHarvests;
    public string? LastDecryptError;
    // Last few distinct CONNECT targets seen, most-recent-first, each "host (decrypt=true|false)". Lets the
    // status surface show WHAT the phone is CONNECTing to when auxbrain is "not reached".
    public IReadOnlyList<string> RecentConnects { get; private set; } = [];

    private const int MaxRecent = 12;
    private readonly object _connectLock = new();
    private readonly LinkedList<string> _recent = new();

    public static readonly DeviceCaptureDiag Empty = new();

    public void Bump(ref long counter) => System.Threading.Interlocked.Increment(ref counter);

    // Record a CONNECT target, de-duplicated and capped most-recent-first. Cheap: one lock, bounded list.
    public void NoteConnect(string host, bool willDecrypt)
    {
        var entry = $"{host} (decrypt={willDecrypt.ToString().ToLowerInvariant()})";
        lock (_connectLock)
        {
            _recent.Remove(entry); // move-to-front on repeat
            _recent.AddFirst(entry);
            while (_recent.Count > MaxRecent) _recent.RemoveLast();
            RecentConnects = _recent.ToList();
        }
    }

    public DeviceCaptureDiag Snapshot()
    {
        var snap = new DeviceCaptureDiag
        {
            ClientConnects = System.Threading.Interlocked.Read(ref ClientConnects),
            AuxbrainConnects = System.Threading.Interlocked.Read(ref AuxbrainConnects),
            Flows = System.Threading.Interlocked.Read(ref Flows),
            RinfoHarvests = System.Threading.Interlocked.Read(ref RinfoHarvests),
            LastDecryptError = LastDecryptError,
        };
        lock (_connectLock) snap.RecentConnects = _recent.ToList();
        return snap;
    }
}
