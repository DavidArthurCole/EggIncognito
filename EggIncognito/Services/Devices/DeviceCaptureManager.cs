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
    ILogger<DeviceCaptureManager> logger) : IHostedService
{
    private readonly Func<bool, ICaptureProxy> _proxyFactory = proxyFactory ?? (verbose => new UnobtaniumCaptureProxy(verbose));
    private readonly ConcurrentDictionary<string, DeviceCapture> _captures = new();
    private readonly DeviceRinfoStore _rinfo = new(capturePath);
    private CancellationTokenSource? _cts;

    // Per-device listener: its proxy, dedicated port, and the harvest pump.
    private sealed record DeviceCapture(ICaptureProxy Proxy, int Port, Channel<CapturedFlow> Queue, Task Pump);

    public int PortFor(string deviceId) => _captures.TryGetValue(deviceId, out var c) ? c.Port : 0;
    public DeviceRinfoStore Rinfo => _rinfo;

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

            var proxy = _proxyFactory(false);
            proxy.FlowCaptured += flow => queue.Writer.TryWrite(flow);
            await proxy.StartAsync(port, caPath, ct);

            _captures[d.Id] = new DeviceCapture(proxy, port, queue, pump);
            logger.LogInformation("device capture: {Id} listening on :{Port} (CA {Ca})", d.Id, port, caPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "device capture: {Id} failed to start on :{Port}", d.Id, port);
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
                if (obs is not null) _rinfo.Observe(deviceId, obs, DateTimeOffset.UtcNow.ToString("O"));
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
