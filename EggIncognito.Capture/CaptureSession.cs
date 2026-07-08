using System.Threading.Channels;
using EggIncognito.Services;

namespace EggIncognito.Capture;

public enum CaptureState { Stopped, Starting, Running, Stopping }

public sealed record CaptureStartResult(bool Running, int Port, string CaPath, bool FreshCa, string? RootThumbprint);

public sealed record CaptureSessionStatus(
    bool Running, int Port, int ActiveClients, string? RootThumbprint, bool CaDmFailed = false);

// Thread-safe, idempotent owner of the capture proxy lifecycle. Owns the flow queue and consumer pump.
// The Hub persists across start/stop so the SPA stays connected.
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
    // Written from proxy event threads, read by Status without taking _gate; volatile keeps the cross-thread read coherent.
    private volatile int _activeClients;

    public CaptureHub Hub { get; } = new();
    public CaptureState State { get; private set; } = CaptureState.Stopped;

    // The proxy's loopback listener sits at Port + 1.
    public int Port => _opts.Port;
    public string CaPath => _opts.CaPath;

    // LastFlowUtc bumps on each captured flow so idle sessions can be reaped. Internal setters are test seams.
    public DateTimeOffset StartedUtc { get; internal set; }
    public DateTimeOffset LastFlowUtc { get; internal set; }

    // Set when a fresh CA was minted this session but the Discord DM could not be delivered.
    public bool CaDmFailed { get; set; }

    public CaptureSession(string contentRoot, CaptureSessionOptions opts, Func<bool, ICaptureProxy>? proxyFactory = null)
    {
        _contentRoot = contentRoot;
        _opts = opts;
        _proxyFactory = proxyFactory ?? (verbose => new NativeCaptureProxy(verbose));
    }

    public CaptureSessionStatus Status =>
        new(State == CaptureState.Running, _opts.Port, _activeClients, _proxy?.RootThumbprint, CaDmFailed);

    public async Task<CaptureStartResult> StartAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (State is CaptureState.Running or CaptureState.Starting)
                return new CaptureStartResult(true, _opts.Port, _opts.CaPath, _proxy?.FreshCa ?? false, _proxy?.RootThumbprint);
            State = CaptureState.Starting;
        }

        try
        {
            Directory.CreateDirectory(_opts.CapturePath);
            _harPath = UniquePath(Path.Combine(_opts.CapturePath, _opts.HarFileName()));

            var deviceStore = new DeviceStore(_opts.CapturePath);
            Hub.SeedKnownDevices(deviceStore.Load());
            Hub.DevicesChanged = () => deviceStore.Save(Hub.SnapshotRememberedDevices());

            var liveVersions = new LiveVersionStore(_opts.CapturePath);

            // Hosted: no endpoint file writes; saves go to the DB store.
            if (_opts.WriteEndpoints)
            {
                _extractor = EndpointExtractor.ForRepo(_contentRoot, _opts.Eid, EidPlaceholder, _opts.Overwrite);
                _extractor.Quiet = true;
            }
            _har = new HarWriter();
            var decoder = new FlowDecoder(_contentRoot);
            var processor = new FlowProcessor(_extractor, decoder, _har, _contentRoot);

            var queue = Channel.CreateUnbounded<CapturedFlow>(new UnboundedChannelOptions { SingleReader = true });
            _queue = queue;

            var proxy = _proxyFactory(_opts.Verbose);
            proxy.FlowCaptured += flow => queue.Writer.TryWrite(flow);
            proxy.ClientConnected += (count, ip) => { _activeClients = count; Hub.RecordConnection(count, ip, Now()); };
            proxy.ClientDisconnected += (count, ip) => { _activeClients = count; Hub.RecordDisconnection(count, Now()); };
            proxy.AuxbrainConnect += () => Hub.RecordAuxbrainConnect();
            proxy.DecryptError += msg => Hub.RecordDecryptError(msg, Now());
            _proxy = proxy;

            _consumer = Task.Run(async () =>
            {
                await foreach (var flow in queue.Reader.ReadAllAsync())
                {
                    LastFlowUtc = DateTimeOffset.UtcNow;
                    try
                    {
                        var dash = processor.Process(flow);
                        if (dash.Observed is { } obs) liveVersions.Observe(obs, DateTimeOffset.UtcNow.ToString("O"));
                        Hub.Publish(dash, Now());
                    }
                    catch { /* a single bad flow must not kill the pump */ }
                }
            });

            await proxy.StartAsync(_opts.Port, _opts.CaPath, ct);
            lock (_gate) { State = CaptureState.Running; }
            StartedUtc = DateTimeOffset.UtcNow;
            LastFlowUtc = StartedUtc; // idle window measures from start until the first flow
            Hub.SetProxyState(running: true, port: _opts.Port); // push running state to dashboards
            return new CaptureStartResult(true, _opts.Port, _opts.CaPath, proxy.FreshCa, proxy.RootThumbprint);
        }
        catch
        {
            // Tear down whatever started so the session is restartable, not wedged at Starting.
            Hub.DevicesChanged = null;
            _queue?.Writer.TryComplete();
            if (_consumer is not null) { try { await _consumer; } catch { } }
            if (_proxy is not null) { try { await _proxy.DisposeAsync(); } catch { } }
            _proxy = null; _queue = null; _consumer = null; _har = null; _extractor = null;
            lock (_gate) { State = CaptureState.Stopped; }
            throw;
        }
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

        try
        {
            if (proxy is not null) await proxy.StopAsync();
            queue?.Writer.TryComplete();
            if (consumer is not null) await consumer;

            if (_har is { Count: > 0 } && _harPath is not null) _har.Save(_harPath);
            _extractor?.Save();
            new DeviceStore(_opts.CapturePath).Save(Hub.SnapshotRememberedDevices()); // final persist
        }
        finally
        {
            // Stop failure must not wedge State; always reach Stopped. Drop DeviceStore sub; StartAsync rewires it.
            Hub.DevicesChanged = null;
            queue?.Writer.TryComplete();
            if (proxy is not null) { try { await proxy.DisposeAsync(); } catch { } }
            lock (_gate)
            {
                _proxy = null; _queue = null; _consumer = null; _har = null; _extractor = null; _activeClients = 0;
                State = CaptureState.Stopped;
            }
            Hub.SetProxyState(running: false, port: _opts.Port); // push stopped state to dashboards
        }
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
