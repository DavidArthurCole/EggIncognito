using EggIncognito.Capture;
using EggIncognito.Services.DataApi;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.Devices.Fake;

public sealed class FakeCaptureProxyFactory(
    FakeDeviceSettings settings,
    FakeDeviceVersions versions,
    FakeFixtureSource fixtures,
    IEndpointStore endpoints,
    IRouteCatalog routes,
    ILoggerFactory logs) {
    private int _issued = -1;

    public ICaptureProxy Create(bool verbose) {
        var devices = settings.Devices;
        int index = Interlocked.Increment(ref _issued) & int.MaxValue;
        var device = devices.Count == 0 ? null : devices[index % devices.Count];
        return new FakeCaptureProxy(device, versions, fixtures, endpoints, routes, settings.CaptureIntervalMs,
            logs.CreateLogger<FakeCaptureProxy>()) { Verbose = verbose };
    }
}

public sealed class FakeCaptureProxy(
    FakeDevice? device,
    FakeDeviceVersions versions,
    FakeFixtureSource fixtures,
    IEndpointStore endpoints,
    IRouteCatalog routes,
    int intervalMs,
    ILogger logger) : ICaptureProxy {
    private const string AuxbrainHost = "www.auxbrain.com";
    private const string PathContracts = "ei/get_contracts";
    private const int MinIntervalMs = 250;

    private static readonly string[] Paths =
        [DataCatalog.ConfigRoute, DataCatalog.PeriodicalsRoute, PathContracts];

    private readonly HashSet<string> _unreplayable = [with(StringComparer.Ordinal)];

    private CancellationTokenSource? _cts;
    private Task? _pump;

    public bool Verbose { get; set; }
    public bool FreshCa => false;
    public string? RootThumbprint => null;

    public event Action<CapturedFlow>? FlowCaptured;
    public event Action<int, string?>? ClientConnected;
    public event Action<int, string?>? ClientDisconnected;
    public event Action? AuxbrainConnect;
    public event Action<string, bool>? ConnectSeen;
    public event Action<string>? Trace;
#pragma warning disable CS0067
    public event Action<string>? DecryptError;
    public event Action? TrustRestored;
#pragma warning restore CS0067

    public Task StartAsync(int port, string caPath, CancellationToken ct) {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        logger.LogInformation("fake capture: {Id} bound no socket, replaying fixtures every {Ms}ms",
            device?.Id ?? "?", intervalMs);
        Trace?.Invoke($"fake capture proxy for port {port} binds no socket");
        ClientConnected?.Invoke(1, "127.0.0.1");
        _pump = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync() {
        if (_cts is { } cts) await cts.CancelAsync();
        ClientDisconnected?.Invoke(0, "127.0.0.1");
        if (_pump is not { } pump) return;
        try {
            await pump;
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "fake capture: {Id} pump cancelled", device?.Id ?? "?");
        }
    }

    public async ValueTask DisposeAsync() {
        await StopAsync();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task PumpAsync(CancellationToken ct) {
        var delay = TimeSpan.FromMilliseconds(Math.Max(MinIntervalMs, intervalMs));
        int index = 0;
        try {
            while (!ct.IsCancellationRequested) {
                await Task.Delay(delay, ct);
                await ReplayAsync(Paths[index++ % Paths.Length], ct);
            }
        } catch (OperationCanceledException ex) {
            logger.LogDebug(ex, "fake capture: {Id} replay stopped", device?.Id ?? "?");
        } catch (Exception ex) {
            logger.LogWarning(ex, "fake capture: {Id} replay threw", device?.Id ?? "?");
        }
    }

    private async Task ReplayAsync(string path, CancellationToken ct) {
        CapturedFlow? flow;
        try {
            flow = await BuildAsync(path, ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            if (_unreplayable.Add(path)) {
                logger.LogWarning(ex, "fake capture: {Id} cannot replay {Path}; skipping it for this session",
                    device?.Id ?? "?", path);
            }
            return;
        }

        if (flow is null) return;
        ConnectSeen?.Invoke(AuxbrainHost, true);
        AuxbrainConnect?.Invoke();
        FlowCaptured?.Invoke(flow);
    }

    private async Task<CapturedFlow?> BuildAsync(string path, CancellationToken ct) {
        if (device is null) return null;
        if (routes.Resolve(path)?.Response is not { } responseType) return null;
        if (ExtractorConfig.EiAssembly.GetType($"Ei.{responseType}") is not { } clr) return null;

        var response = endpoints.Fetch(clr, path);
        var installed = await fixtures.ResolveAsync(device, versions, ct);
        return new CapturedFlow(
            $"https://{AuxbrainHost}/{path}",
            "POST",
            200,
            Request(path, device, installed),
            Convert.ToBase64String(response.ToByteArray()),
            [new HttpHeader("content-type", "application/x-www-form-urlencoded")],
            [new HttpHeader("content-type", "text/html")]);
    }

    private static string? Request(string path, FakeDevice fake, FakeInstalledVersion installed) {
        var rinfo = new BasicRequestInfo { Platform = fake.Platform };
        if (installed.AppVersion is { } version) rinfo.Version = version;
        if (installed.Build is { } build) rinfo.Build = build;
        if (installed.ClientVersion is { } clientVersion) rinfo.ClientVersion = (uint)clientVersion;

        IMessage? request = path switch {
            DataCatalog.ConfigRoute => new ConfigRequest { Rinfo = rinfo },
            DataCatalog.PeriodicalsRoute => new GetPeriodicalsRequest { Rinfo = rinfo },
            PathContracts => new ContractsRequest {
                ClientVersion = (uint)(installed.ClientVersion ?? 1)
            },
            _ => null
        };
        return request is null ? null : Convert.ToBase64String(request.ToByteArray());
    }
}
