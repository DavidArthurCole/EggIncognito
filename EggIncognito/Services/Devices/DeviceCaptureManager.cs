using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public sealed class DeviceCaptureManager(
    DeviceCaptureConfig config,
    DeviceConfig devices,
    string capturePath,
    string caPath,
    Func<bool, ICaptureProxy>? proxyFactory,
    string contentRoot,
    ILogger<DeviceCaptureManager> logger,
    IEnumerable<IDeviceCaInstaller>? caInstallers = null,
    IReadOnlySet<string>? liveRoutes = null,
    IEndpointWriteObserver? writeObserver = null,
    IRouteCatalog? catalog = null,
    IProcessedFlowObserver? flowObserver = null) : IHostedService, IDisposable {
    public const int PortsPerDevice = 3;

    private readonly Dictionary<string, IDeviceCaInstaller> _caInstallers =
        (caInstallers ?? []).GroupBy(c => c.Platform, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DeviceCapture> _captures = new();
    private readonly ConcurrentDictionary<string, DeviceCaptureDiag> _diag = new();

    private readonly Func<bool, ICaptureProxy> _proxyFactory =
        proxyFactory ?? (verbose => new NativeCaptureProxy(verbose));

    private CancellationTokenSource? _cts;
    public DeviceRinfoStore Rinfo { get; } = new(capturePath);

    public async Task StartAsync(CancellationToken cancellationToken) {
        if (!config.Enabled) {
            logger.LogInformation("device capture: disabled (DeviceCapture:Enabled=false)");
            return;
        }

        if (devices.Devices.Count == 0) {
            logger.LogInformation("device capture: no devices declared");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await EnsureAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken) {
        _cts?.Cancel();
        foreach (string id in _captures.Keys.ToList()) await TeardownAsync(id);
    }

    public void Dispose() {
        _cts?.Dispose();
        _cts = null;
    }

    public int PortFor(string deviceId) => _captures.TryGetValue(deviceId, out var c) ? c.Port : 0;

    public CaptureHub? HubFor(string deviceId) => _captures.TryGetValue(deviceId, out var c) ? c.Hub : null;

    private static string Now() => DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public DeviceCaptureDiag DiagFor(string deviceId) =>
        _diag.TryGetValue(deviceId, out var d) ? d.Snapshot() : DeviceCaptureDiag.Empty;

    public static int PortForIndex(int basePort, int index) => basePort + index * PortsPerDevice;

    public async Task EnsureAsync(CancellationToken ct) {
        if (!config.Enabled) return;
        for (int i = 0; i < devices.Devices.Count; i++) {
            var d = devices.Devices[i];
            if (_captures.ContainsKey(d.Id)) continue;
            await StartOneAsync(d, PortForIndex(config.BasePort, i), ct);
        }
    }

    private async Task StartOneAsync(DeviceEntry d, int port, CancellationToken ct) {
        try {
            var hub = new CaptureHub();
            var pipeline = new CapturePipeline(contentRoot, null, false, false, liveRoutes, writeObserver, null, catalog);
            var pump = pipeline.StartPump(hub, Now, null, obs => {
                Rinfo.Observe(d.Id, obs, DateTimeOffset.UtcNow.ToString("O"));
                if (_diag.TryGetValue(d.Id, out var dg)) dg.BumpRinfoHarvests();
            }, flowObserver is { } observer ? dash => observer.OnFlowProcessed(d.Id, dash) : null, ct);

            var diag = _diag.GetOrAdd(d.Id, _ => new DeviceCaptureDiag());

            var proxy = _proxyFactory(config.Verbose);
            if (config.Verbose)
                proxy.Trace += line => logger.LogDebug("device capture: {Id} trace: {Line}", d.Id, line);
            pipeline.Attach(proxy, hub, Now,
                onFlowCaptured: _ => diag.BumpFlows(),
                onClientConnected: (count, ip) => {
                    diag.BumpClientConnects();
                    logger.LogInformation("device capture: {Id} client connected (active={Count}, ip={Ip})", d.Id,
                        count, ip ?? "?");
                },
                onAuxbrainConnect: () => {
                    diag.BumpAuxbrainConnects();
                    logger.LogDebug("device capture: {Id} auxbrain CONNECT decrypted", d.Id);
                },
                onDecryptError: msg => {
                    diag.LastDecryptError = msg;
                    logger.LogWarning("device capture: {Id} decrypt error: {Msg}", d.Id, msg);
                },
                onTrustRestored: () => {
                    diag.LastDecryptError = null;
                    logger.LogInformation("device capture: {Id} decryption recovered", d.Id);
                });
            proxy.ConnectSeen += (host, willDecrypt) => diag.NoteConnect(host, willDecrypt);
            await proxy.StartAsync(port, caPath, ct);
            hub.SetProxyState(true, port);

            _captures[d.Id] = new DeviceCapture(proxy, port, pipeline.Queue, pump, hub);
            logger.LogInformation("device capture: {Id} listening on :{Port} (CA {Ca}, freshCa={Fresh})",
                d.Id, port, caPath, proxy.FreshCa);

            if (proxy.FreshCa)
                logger.LogWarning(
                    "device capture: {Id} FRESH CA minted ({Ca}). Any cert installed on a device last run is now " +
                    "STALE. Persist the captures dir across restarts or device trust resets on every deploy.", d.Id,
                    caPath);

            await InstallCaAsync(d, ct);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device capture: {Id} failed to start on :{Port}", d.Id, port);
        }
    }

    public async Task<(bool Ok, string? Note)> InstallCaAsync(DeviceEntry d, CancellationToken ct) {
        if (!_caInstallers.TryGetValue(d.Platform, out var installer))
            return (false, $"no CA installer for {d.Platform}");
        try {
            var target = new DeviceTarget(d.Id, d.Platform, d.Target, d.Package);
            (bool ok, string? note) = await installer.InstallAsync(target, caPath, ct);
            if (ok) logger.LogInformation("device capture: {Id} CA installed ({Note})", d.Id, note);
            else logger.LogWarning("device capture: {Id} CA install failed: {Note}", d.Id, note);
            return (ok, note);
        } catch (Exception ex) {
            logger.LogWarning(ex, "device capture: {Id} CA install threw", d.Id);
            return (false, ex.Message);
        }
    }

    private async Task TeardownAsync(string id) {
        if (!_captures.TryRemove(id, out var c)) return;
        c.Hub.SetProxyState(false, c.Port);
        try {
            await c.Proxy.StopAsync();
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} proxy stop threw during teardown", id);
        }

        c.Queue.Writer.TryComplete();
        try {
            await c.Pump;
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} pump faulted during teardown", id);
        }

        try {
            await c.Proxy.DisposeAsync();
        } catch (Exception ex) {
            logger.LogDebug(ex, "device capture: {Id} proxy dispose threw during teardown", id);
        }

        logger.LogInformation("device capture: {Id} torn down", id);
    }

    private sealed record DeviceCapture(
        ICaptureProxy Proxy,
        int Port,
        Channel<CapturedFlow> Queue,
        Task Pump,
        CaptureHub Hub);
}

public sealed class DeviceCaptureDiag {
    private const int MaxRecent = 12;

    public static readonly DeviceCaptureDiag Empty = new();
    private readonly Lock _connectLock = new();
    private readonly LinkedList<string> _recent = new();
    private long _auxbrainConnects;
    private long _clientConnects;
    private long _flows;
    private long _rinfoHarvests;

    public long ClientConnects {
        get => Interlocked.Read(ref _clientConnects);
        private set => _clientConnects = value;
    }

    public long AuxbrainConnects {
        get => Interlocked.Read(ref _auxbrainConnects);
        private set => _auxbrainConnects = value;
    }

    public long Flows {
        get => Interlocked.Read(ref _flows);
        private set => _flows = value;
    }

    public long RinfoHarvests {
        get => Interlocked.Read(ref _rinfoHarvests);
        private set => _rinfoHarvests = value;
    }

    public string? LastDecryptError { get; set; }

    public IReadOnlyList<string> RecentConnects { get; private set; } = [];

    public void BumpClientConnects() => Interlocked.Increment(ref _clientConnects);
    public void BumpAuxbrainConnects() => Interlocked.Increment(ref _auxbrainConnects);
    public void BumpFlows() => Interlocked.Increment(ref _flows);
    public void BumpRinfoHarvests() => Interlocked.Increment(ref _rinfoHarvests);

    public void NoteConnect(string host, bool willDecrypt) {
        string entry = $"{host} (decrypt={(willDecrypt ? "true" : "false")})";
        lock (_connectLock) {
            _recent.Remove(entry);
            _recent.AddFirst(entry);
            while (_recent.Count > MaxRecent) _recent.RemoveLast();
            RecentConnects = _recent.ToList();
        }
    }

    public DeviceCaptureDiag Snapshot() {
        var snap = new DeviceCaptureDiag {
            ClientConnects = ClientConnects,
            AuxbrainConnects = AuxbrainConnects,
            Flows = Flows,
            RinfoHarvests = RinfoHarvests,
            LastDecryptError = LastDecryptError
        };
        lock (_connectLock) snap.RecentConnects = _recent.ToList();
        return snap;
    }
}
