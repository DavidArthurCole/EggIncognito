using System.Threading.Channels;
using EggIncognito.Services;

namespace EggIncognito.Capture;

public enum CaptureState {
    Stopped,
    Starting,
    Running,
    Stopping
}

public sealed record CaptureStartResult(bool Running, int Port, string CaPath, bool FreshCa, string? RootThumbprint);

public sealed record CaptureSessionStatus(
    bool Running,
    int Port,
    int ActiveClients,
    string? RootThumbprint,
    bool CaDmFailed = false);

public sealed class CaptureSession(
    string contentRoot,
    CaptureSessionOptions opts,
    Func<bool, ICaptureProxy>? proxyFactory = null) {
    private const string EidPlaceholder = "EI0000000000000000";

    private readonly Lock _gate = new();

    private readonly Func<bool, ICaptureProxy> _proxyFactory =
        proxyFactory ?? (verbose => new NativeCaptureProxy(verbose));

    private volatile int _activeClients;
    private Task? _consumer;
    private EndpointExtractor? _extractor;
    private HarWriter? _har;
    private string? _harPath;

    private ICaptureProxy? _proxy;
    private Channel<CapturedFlow>? _queue;

    public CaptureHub Hub { get; } = new();
    public CaptureState State { get; private set; } = CaptureState.Stopped;


    public int Port => opts.Port;
    public string CaPath => opts.CaPath;


    public DateTimeOffset StartedUtc { get; internal set; }
    public DateTimeOffset LastFlowUtc { get; internal set; }


    public bool CaDmFailed { get; set; }

    public CaptureSessionStatus Status =>
        new(State == CaptureState.Running, opts.Port, _activeClients, _proxy?.RootThumbprint, CaDmFailed);

    public async Task<CaptureStartResult> StartAsync(CancellationToken ct) {
        lock (_gate) {
            if (State is CaptureState.Running or CaptureState.Starting) {
                return new CaptureStartResult(true, opts.Port, opts.CaPath, _proxy?.FreshCa ?? false,
                    _proxy?.RootThumbprint);
            }

            State = CaptureState.Starting;
        }

        try {
            Directory.CreateDirectory(opts.CapturePath);
            _harPath = UniquePath(Path.Combine(opts.CapturePath, opts.HarFileName()));

            var deviceStore = new DeviceStore(opts.CapturePath);
            Hub.SeedKnownDevices(deviceStore.Load());
            Hub.DevicesChanged = () => deviceStore.Save(Hub.SnapshotRememberedDevices());

            var liveVersions = new LiveVersionStore(opts.CapturePath);


            if (opts.WriteEndpoints || opts.LiveRoutes.Count > 0) {
                _extractor = EndpointExtractor.ForRepo(contentRoot, opts.Eid, EidPlaceholder, opts.Overwrite);
                _extractor.Quiet = true;
                _extractor.LiveOnly = !opts.WriteEndpoints;
                _extractor.LiveRoutes = opts.LiveRoutes.ToHashSet(StringComparer.Ordinal);
                _extractor.WriteObserver = opts.WriteObserver;
            }

            _har = new HarWriter();
            var decoder = new FlowDecoder(contentRoot);
            var processor = new FlowProcessor(_extractor, decoder, _har, contentRoot);

            var queue = Channel.CreateUnbounded<CapturedFlow>(new UnboundedChannelOptions { SingleReader = true });
            _queue = queue;

            var proxy = _proxyFactory(opts.Verbose);
            proxy.FlowCaptured += flow => queue.Writer.TryWrite(flow);
            proxy.ClientConnected += (count, ip) => {
                _activeClients = count;
                Hub.RecordConnection(count, ip, Now());
            };
            proxy.ClientDisconnected += (count, ip) => {
                _activeClients = count;
                Hub.RecordDisconnection(count, Now());
            };
            proxy.AuxbrainConnect += () => Hub.RecordAuxbrainConnect();
            proxy.DecryptError += msg => Hub.RecordDecryptError(msg, Now());
            proxy.TrustRestored += () => Hub.RecordTrustRestored(Now());
            _proxy = proxy;

            _consumer = Task.Run(async () => {
                await foreach (var flow in queue.Reader.ReadAllAsync()) {
                    LastFlowUtc = DateTimeOffset.UtcNow;
                    try {
                        var dash = processor.Process(flow);
                        if (dash.Observed is { } obs) liveVersions.Observe(obs, DateTimeOffset.UtcNow.ToString("O"));
                        Hub.Publish(dash, Now());
                    } catch {
                        /* a single bad flow must not kill the pump */
                    }
                }
            }, ct);

            await proxy.StartAsync(opts.Port, opts.CaPath, ct);
            lock (_gate) State = CaptureState.Running;
            StartedUtc = DateTimeOffset.UtcNow;
            LastFlowUtc = StartedUtc;
            Hub.SetProxyState(true, opts.Port);
            return new CaptureStartResult(true, opts.Port, opts.CaPath, proxy.FreshCa, proxy.RootThumbprint);
        } catch {
            Hub.DevicesChanged = null;
            _queue?.Writer.TryComplete();
            if (_consumer is not null) {
                try {
                    await _consumer;
                } catch {
                }
            }

            if (_proxy is not null) {
                try {
                    await _proxy.DisposeAsync();
                } catch {
                }
            }

            _proxy = null;
            _queue = null;
            _consumer = null;
            _har = null;
            _extractor = null;
            lock (_gate) State = CaptureState.Stopped;
            throw;
        }
    }

    public async Task StopAsync() {
        ICaptureProxy? proxy;
        Channel<CapturedFlow>? queue;
        Task? consumer;
        lock (_gate) {
            if (State is CaptureState.Stopped or CaptureState.Stopping) return;
            State = CaptureState.Stopping;
            proxy = _proxy;
            queue = _queue;
            consumer = _consumer;
        }

        try {
            if (proxy is not null) await proxy.StopAsync();
            queue?.Writer.TryComplete();
            if (consumer is not null) await consumer;

            if (_har is { Count: > 0 } && _harPath is not null) _har.Save(_harPath);
            _extractor?.Save();
            new DeviceStore(opts.CapturePath).Save(Hub.SnapshotRememberedDevices());
        } finally {
            Hub.DevicesChanged = null;
            queue?.Writer.TryComplete();
            if (proxy is not null) {
                try {
                    await proxy.DisposeAsync();
                } catch {
                }
            }

            lock (_gate) {
                _proxy = null;
                _queue = null;
                _consumer = null;
                _har = null;
                _extractor = null;
                _activeClients = 0;
                State = CaptureState.Stopped;
            }

            Hub.SetProxyState(false, opts.Port);
        }
    }


    public (string? Json, string? Type, bool Known) Decode(string path, string responseB64) {
        var decoder = new FlowDecoder(contentRoot);
        var r = decoder.DecodeResponse(path, responseB64);
        return (r.Json, r.Type, r.Known);
    }


    public string? SaveEndpoint(string path, string method, int status, string? requestDataB64, string responseB64) {
        var ex = _extractor;
        if (ex is null) return null;
        string url = $"https://www.auxbrain.com/{path}";
        string? written = ex.ForceWriteEndpoint(url, method, status, requestDataB64, responseB64);
        ex.Save();
        return written;
    }


    public string CurrentHar() => _har?.ToHar() ?? new HarWriter().ToHar();

    private static string Now() => DateTime.Now.ToString("HH:mm:ss");

    internal static string UniquePath(string path) {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++) {
            string candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
