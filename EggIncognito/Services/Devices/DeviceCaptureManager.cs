using System.Collections.Concurrent;
using System.Threading.Channels;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Services;

namespace EggIncognito.Services.Devices;

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
    private readonly Func<bool, ICaptureProxy> _proxyFactory = proxyFactory ?? (verbose => new NativeCaptureProxy(verbose));
    private readonly ConcurrentDictionary<string, DeviceCapture> _captures = new();
    private readonly ConcurrentDictionary<string, DeviceCaptureDiag> _diag = new();
    private readonly DeviceRinfoStore _rinfo = new(capturePath);
    private readonly IReadOnlyDictionary<string, EggIncognito.Core.Services.Devices.IDeviceCaInstaller> _caInstallers =
        (caInstallers ?? []).GroupBy(c => c.Platform, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;

    private sealed record DeviceCapture(ICaptureProxy Proxy, int Port, Channel<CapturedFlow> Queue, Task Pump, CaptureHub Hub);

    public int PortFor(string deviceId) => _captures.TryGetValue(deviceId, out var c) ? c.Port : 0;
    public DeviceRinfoStore Rinfo => _rinfo;



    public CaptureHub? HubFor(string deviceId) => _captures.TryGetValue(deviceId, out var c) ? c.Hub : null;

    private static string Now() => DateTime.Now.ToString("HH:mm:ss");

   
   
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

   
   
    public const int PortsPerDevice = 3;

    public static int PortForIndex(int basePort, int index) => basePort + index * PortsPerDevice;

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
            var hub = new CaptureHub();
            var queue = Channel.CreateUnbounded<CapturedFlow>(new UnboundedChannelOptions { SingleReader = true });
            var decoder = new FlowDecoder(contentRoot);
            var processor = new FlowProcessor(null, decoder, null, contentRoot);
            var pump = Task.Run(() => PumpAsync(d.Id, queue, processor, hub), ct);

            var diag = _diag.GetOrAdd(d.Id, _ => new DeviceCaptureDiag());

            var proxy = _proxyFactory(config.Verbose);
            if (config.Verbose)
                proxy.Trace += line => logger.LogDebug("device capture: {Id} trace: {Line}", d.Id, line);
            proxy.FlowCaptured += flow =>
            {
                diag.Bump(ref diag.Flows);
                queue.Writer.TryWrite(flow);
            };
            proxy.ClientConnected += (count, ip) =>
            {
                diag.Bump(ref diag.ClientConnects);
                hub.RecordConnection(count, ip, Now());
                logger.LogInformation("device capture: {Id} client connected (active={Count}, ip={Ip})", d.Id, count, ip ?? "?");
            };
            proxy.ClientDisconnected += (count, ip) => hub.RecordDisconnection(count, Now());
            proxy.AuxbrainConnect += () =>
            {
                diag.Bump(ref diag.AuxbrainConnects);
                hub.RecordAuxbrainConnect();
                logger.LogDebug("device capture: {Id} auxbrain CONNECT decrypted", d.Id);
            };
            proxy.DecryptError += msg =>
            {
                diag.LastDecryptError = msg;
                hub.RecordDecryptError(msg, Now());
                logger.LogWarning("device capture: {Id} decrypt error: {Msg}", d.Id, msg);
            };
            proxy.ConnectSeen += (host, willDecrypt) => diag.NoteConnect(host, willDecrypt);
            await proxy.StartAsync(port, caPath, ct);
            hub.SetProxyState(running: true, port: port);

            _captures[d.Id] = new DeviceCapture(proxy, port, queue, pump, hub);
            logger.LogInformation("device capture: {Id} listening on :{Port} (CA {Ca}, freshCa={Fresh})",
                d.Id, port, caPath, proxy.FreshCa);

           
            if (proxy.FreshCa)
                logger.LogWarning(
                    "device capture: {Id} FRESH CA minted ({Ca}). Any cert installed on a device last run is now " +
                    "STALE. Persist the captures dir across restarts or device trust resets on every deploy.", d.Id, caPath);

            await InstallCaAsync(d, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "device capture: {Id} failed to start on :{Port}", d.Id, port);
        }
    }

   
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

    private async Task PumpAsync(string deviceId, Channel<CapturedFlow> queue, FlowProcessor processor, CaptureHub hub)
    {
        await foreach (var flow in queue.Reader.ReadAllAsync())
        {
            try
            {
                var dash = processor.Process(flow);
                if (dash.Observed is { } obs)
                {
                    _rinfo.Observe(deviceId, obs, DateTimeOffset.UtcNow.ToString("O"));
                    if (_diag.TryGetValue(deviceId, out var dg)) dg.Bump(ref dg.RinfoHarvests);
                }
                hub.Publish(dash, Now());
            }
            catch { }
        }
    }

    private async Task TeardownAsync(string id)
    {
        if (!_captures.TryRemove(id, out var c)) return;
        c.Hub.SetProxyState(running: false, port: c.Port);
        try { await c.Proxy.StopAsync(); } catch { }
        c.Queue.Writer.TryComplete();
        try { await c.Pump; } catch { }
        try { await c.Proxy.DisposeAsync(); } catch { }
        logger.LogInformation("device capture: {Id} torn down", id);
    }
}

public sealed class DeviceCaptureDiag
{
    public long ClientConnects;
    public long AuxbrainConnects;
    public long Flows;
    public long RinfoHarvests;
    public string? LastDecryptError;
   
    public IReadOnlyList<string> RecentConnects { get; private set; } = [];

    private const int MaxRecent = 12;
    private readonly object _connectLock = new();
    private readonly LinkedList<string> _recent = new();

    public static readonly DeviceCaptureDiag Empty = new();

    public void Bump(ref long counter) => System.Threading.Interlocked.Increment(ref counter);

    public void NoteConnect(string host, bool willDecrypt)
    {
        var entry = $"{host} (decrypt={willDecrypt.ToString().ToLowerInvariant()})";
        lock (_connectLock)
        {
            _recent.Remove(entry);
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
