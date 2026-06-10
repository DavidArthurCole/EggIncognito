using System.Threading.Channels;
using EggIncognito.Services;

namespace EggIncognito.Capture;

public enum CaptureState { Stopped, Starting, Running, Stopping }

public sealed record CaptureStartResult(bool Running, int Port, string CaPath, bool FreshCa, string? RootThumbprint);

public sealed record CaptureSessionStatus(bool Running, int Port, int ActiveClients, string? RootThumbprint);

// Thread-safe, idempotent owner of the capture proxy lifecycle. Owns the flow queue and consumer pump.
// The Hub persists across start/stop so the SPA stays connected; only the proxy + consumer are created
// on Start and torn down on Stop.
public sealed class CaptureSession
{
    private const string EidPlaceholder = "EI0000000000000000";

    private readonly string _contentRoot;
    private readonly CaptureSessionOptions _opts;
    private readonly Func<bool, ICaptureProxy> _proxyFactory;
    private readonly object _gate = new();

    private ICaptureProxy? _proxy;
    private Channel<CapturedFlow>? _queue;
    private Task? _consumer;
    private HarWriter? _har;
    private EndpointExtractor? _extractor;
    private string? _harPath;
    private int _activeClients;

    public CaptureHub Hub { get; } = new();
    public CaptureState State { get; private set; } = CaptureState.Stopped;

    public CaptureSession(string contentRoot, CaptureSessionOptions opts, Func<bool, ICaptureProxy>? proxyFactory = null)
    {
        _contentRoot = contentRoot;
        _opts = opts;
        _proxyFactory = proxyFactory ?? (verbose => new UnobtaniumCaptureProxy(verbose));
    }

    public CaptureSessionStatus Status =>
        new(State == CaptureState.Running, _opts.Port, _activeClients, _proxy?.RootThumbprint);

    public async Task<CaptureStartResult> StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (State is CaptureState.Running or CaptureState.Starting)
                return new CaptureStartResult(true, _opts.Port, _opts.CaPath, _proxy?.FreshCa ?? false, _proxy?.RootThumbprint);
            State = CaptureState.Starting;
        }

        Directory.CreateDirectory(_opts.CapturePath);
        _harPath = UniquePath(Path.Combine(_opts.CapturePath, _opts.HarFileName()));

        // Seed devices remembered from prior runs, and persist the merged set whenever it changes.
        var deviceStore = new DeviceStore(_opts.CapturePath);
        Hub.SeedKnownDevices(deviceStore.Load());
        Hub.DevicesChanged = () => deviceStore.Save(Hub.SnapshotRememberedDevices());

        _extractor = EndpointExtractor.ForRepo(_contentRoot, _opts.Eid, EidPlaceholder, _opts.Overwrite);
        _extractor.Quiet = true;
        _har = new HarWriter();
        var decoder = new FlowDecoder(_contentRoot);
        var processor = new FlowProcessor(_extractor, decoder, _har, _contentRoot);

        _queue = Channel.CreateUnbounded<CapturedFlow>(new UnboundedChannelOptions { SingleReader = true });

        var proxy = _proxyFactory(_opts.Verbose);
        proxy.FlowCaptured += flow => _queue!.Writer.TryWrite(flow);
        proxy.ClientConnected += (count, ip) => { _activeClients = count; Hub.RecordConnection(count, ip, Now()); };
        proxy.ClientDisconnected += (count, ip) => { _activeClients = count; Hub.RecordDisconnection(count, Now()); };
        proxy.AuxbrainConnect += () => Hub.RecordAuxbrainConnect();
        proxy.DecryptError += msg => Hub.RecordDecryptError(msg, Now());
        _proxy = proxy;

        _consumer = Task.Run(async () =>
        {
            await foreach (var flow in _queue.Reader.ReadAllAsync())
            {
                try { Hub.Publish(processor.Process(flow), Now()); }
                catch { /* a single bad flow must not kill the pump */ }
            }
        });

        await proxy.StartAsync(_opts.Port, _opts.CaPath, ct);
        lock (_gate) { State = CaptureState.Running; }
        Hub.SetProxyState(running: true, port: _opts.Port); // push running state to dashboards
        return new CaptureStartResult(true, _opts.Port, _opts.CaPath, proxy.FreshCa, proxy.RootThumbprint);
    }

    public async Task StopAsync()
    {
        ICaptureProxy? proxy;
        Channel<CapturedFlow>? queue;
        Task? consumer;
        lock (_gate)
        {
            if (State is CaptureState.Stopped or CaptureState.Stopping) return;
            State = CaptureState.Stopping;
            proxy = _proxy; queue = _queue; consumer = _consumer;
        }

        if (proxy is not null) await proxy.StopAsync();
        queue?.Writer.TryComplete();
        if (consumer is not null) await consumer;

        if (_har is { Count: > 0 } && _harPath is not null) _har.Save(_harPath);
        _extractor?.Save();
        new DeviceStore(_opts.CapturePath).Save(Hub.SnapshotRememberedDevices()); // final persist

        if (proxy is not null) await proxy.DisposeAsync();

        lock (_gate)
        {
            _proxy = null; _queue = null; _consumer = null; _har = null; _extractor = null; _activeClients = 0;
            State = CaptureState.Stopped;
        }
        Hub.SetProxyState(running: false, port: _opts.Port); // push stopped state to dashboards
    }

    // Decode an arbitrary path+response for the /decode debug endpoint. Transient decoder, no running
    // session required.
    public (string? Json, string? Type, bool Known) Decode(string path, string responseB64)
    {
        var decoder = new FlowDecoder(_contentRoot);
        var r = decoder.DecodeResponse(path, responseB64);
        return (r.Json, r.Type, r.Known);
    }

    // Force-write a buffered flow as an endpoint. Requires a running session with the extractor present.
    public string? SaveEndpoint(string path, string method, int status, string? requestDataB64, string responseB64)
    {
        var ex = _extractor;
        if (ex is null) return null;
        var url = $"https://www.auxbrain.com/{path}";
        var written = ex.ForceWriteEndpoint(url, method, status, requestDataB64, responseB64);
        ex.Save();
        return written;
    }

    // HAR-so-far for download. Empty HAR JSON when there is no session or no flows.
    public string CurrentHar() => _har?.ToHar() ?? new HarWriter().ToHar();

    private static string Now() => DateTime.Now.ToString("HH:mm:ss");

    internal static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
