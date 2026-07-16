using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unobtanium.Web.Proxy;
using Unobtanium.Web.Proxy.Events;
using Unobtanium.Web.Proxy.Services;
using EggIncognito.Services;

namespace EggIncognito.Capture;

public sealed class UnobtaniumCaptureProxy : ICaptureProxy
{
    private const string RootCaName = "EggIncognito Capture Root";

    private IHost? _host;
    private LanForwarder? _forwarder;
    private readonly ProxyServerEvents _events = new();
    private X509Certificate2? _rootCa;
    private bool _trustAdded;
    private bool _freshCa;

   
   
    private sealed record PendingRequest(DateTime StashedAtUtc, string? Data, IReadOnlyList<HttpHeader> Headers);
    internal static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(2);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();
    private DateTime _lastSweepUtc = DateTime.UtcNow;

   
   
    private static readonly TimeSpan TrustGrace = TimeSpan.FromSeconds(8);
    private readonly object _trustGate = new();
    private bool _trustProven;
    private bool _untrustedReported;
    private Timer? _trustTimer;

    public bool FreshCa => _freshCa;
    public string? RootThumbprint => _rootCa?.Thumbprint;

    public event Action<CapturedFlow>? FlowCaptured;

   
    public event Action<int, string?>? ClientConnected;
    public event Action<int, string?>? ClientDisconnected;
    public event Action? AuxbrainConnect;
    public event Action<string>? DecryptError;
    public event Action<string, bool>? ConnectSeen;

    public bool Verbose { get; set; }

   
   
    public bool LanForwarderEnabled { get; init; } = true;
    public bool TrustCaInOsStore { get; init; } = true;

    public event Action<string>? Trace;
    private void Log(string msg) { if (Verbose) Trace?.Invoke(msg); }

    private int _flowErrors;
    public int FlowErrorCount => Volatile.Read(ref _flowErrors);
    internal void ReportFlowError(string stage, Exception ex)
    {
        Interlocked.Increment(ref _flowErrors);
        Log($"{stage}-ERR {ex.Message}");
        DecryptError?.Invoke($"Capture {stage} handler failed: {ex.Message}");
    }

    public UnobtaniumCaptureProxy(bool verbose = false) => Verbose = verbose;

    public async Task StartAsync(int port, string caPath, CancellationToken ct)
    {
        var certDir = Path.GetDirectoryName(Path.GetFullPath(caPath))!;
        Directory.CreateDirectory(certDir);

       
       
        var caCacheDir = Path.Combine(certDir, ".ca");
        Directory.CreateDirectory(caCacheDir);

        var pfxPath = Path.Combine(caCacheDir, "root.pfx");
        _freshCa = !File.Exists(pfxPath);

        WireEvents();

       
       
        var internalPort = port + 1;
        var internalHttpsPort = port + 2;

        var builder = Host.CreateApplicationBuilder();
       
       
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Verbose ? LogLevel.Warning : LogLevel.None);
        if (Verbose) builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

        builder.Services.AddProxyEvents(_events);
        builder.Services.Configure<ProxyServerOptions>(o =>
        {
            o.Port = internalPort;
            o.HttpsPort = internalHttpsPort;
        });
        builder.Services.Configure<CertificateManagerConfiguration>(o =>
        {
           
            o.CachePath = caCacheDir;
            o.RootCertificateName = RootCaName;
            o.CacheRootCertificate = true;
            o.CacheHostCertificates = false;
        });
        builder.Services.AddProxyServices();

        _host = builder.Build();
        await _host.StartAsync(ct);

        if (LanForwarderEnabled)
        {
            _forwarder = new LanForwarder(publicPort: port, proxyPort: internalPort)
            {
                Trace = Log,
                DeviceConnected = (ip, n) => ClientConnected?.Invoke(n, ip),
                DeviceDisconnected = (ip, n) => ClientDisconnected?.Invoke(n, ip),
            };
            _forwarder.Start();
        }

        var cm = _host.Services.GetRequiredService<ICertificateManager>();
        _rootCa = await cm.GetRootCertificateAsync(includePrivateKey: false, ct);
        await File.WriteAllBytesAsync(caPath, _rootCa.Export(X509ContentType.Cert), ct);
        if (TrustCaInOsStore) TrustRootCa(_rootCa);
    }

    private void WireEvents()
    {
        WireConnectDecision();
        WireRequest();
        WireResponse();
    }

    internal static bool ShouldDecrypt(string connectAuthority) => AuxbrainHosts.IsAuxbrain(connectAuthority);

    private void WireConnectDecision()
    {
        _events.ShouldDecryptNewConnection = (host, client, cts) =>
        {
            var decrypt = ShouldDecrypt(host);
            Log($"CONNECT {host}  decrypt={decrypt}");
            ConnectSeen?.Invoke(host, decrypt);
           
            if (decrypt) { AuxbrainConnect?.Invoke(); ArmTrustInference(host); }
            return Task.FromResult(decrypt);
        };
    }

   
   
    private void WireRequest()
    {
        _events.OnRequest += async (sender, e, token) =>
        {
            try
            {
                SweepStalePending();

                var headers = CollectHeaders(e.Request.Headers, e.Request.Content?.Headers);
                _pendingRequests[e.RequestId] = new PendingRequest(DateTime.UtcNow, null, headers);

                if (e.Request.Content is not null)
                {
                    var bytes = await e.Request.Content.ReadAsByteArrayAsync(token);
                    var data = WireBody.ExtractDataParam(System.Text.Encoding.UTF8.GetString(bytes));
                    if (data is not null) _pendingRequests[e.RequestId] = new PendingRequest(DateTime.UtcNow, data, headers);

                    var fresh = new HttpRequestMessage(e.Request.Method, e.Request.RequestUri);
                    var content = new ByteArrayContent(bytes);
                    foreach (var h in e.Request.Content.Headers)
                        content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    fresh.Content = content;
                    foreach (var h in e.Request.Headers)
                        fresh.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    Log($"REQ  {e.Request.Method} {e.Request.RequestUri}  data={(data is null ? "none" : data.Length + "b64")}");
                    return RequestEventResponse.ModifyRequest(fresh);
                }
            }
            catch (Exception ex) { ReportFlowError("request", ex); }
            return RequestEventResponse.ContinueResponse();
        };
    }

   
    private void WireResponse()
    {
        _events.OnResponse += async (sender, e, token) =>
        {
            try
            {
                var uri = e.Request.RequestUri;
                var host = uri?.Host ?? "";
                if (!AuxbrainHosts.IsAuxbrain(host) || e.Response.Content is null)
                    return ResponseEventResponse.ContinueResponse();

                var respBytes = await e.Response.Content.ReadAsByteArrayAsync(token);
                var (responseB64, shape) = WireBody.Normalize(respBytes);
                var status = (int)e.Response.StatusCode;
                _pendingRequests.TryRemove(e.RequestId, out var pending);
                var reqData = pending?.Data;
                var reqHeaders = pending?.Headers;
                var respHeaders = CollectHeaders(e.Response.Headers, e.Response.Content?.Headers);
                Log($"RESP {host}  status={status}  shape={shape}  len={responseB64.Length}");
                MarkTrustProven();
                FlowCaptured?.Invoke(new CapturedFlow(
                    uri!.ToString(), e.Request.Method.Method, status, reqData, responseB64,
                    reqHeaders, respHeaders));
            }
            catch (Exception ex) { ReportFlowError("response", ex); }
            return ResponseEventResponse.ContinueResponse();
        };
    }

   
    private static IReadOnlyList<HttpHeader> CollectHeaders(
        System.Net.Http.Headers.HttpHeaders messageHeaders,
        System.Net.Http.Headers.HttpHeaders? contentHeaders)
    {
        var list = new List<HttpHeader>();
        void Add(System.Net.Http.Headers.HttpHeaders hs)
        {
            foreach (var h in hs)
                foreach (var v in h.Value)
                    list.Add(new HttpHeader(h.Key, v));
        }
        Add(messageHeaders);
        if (contentHeaders is not null) Add(contentHeaders);
        return list;
    }

   
    private void SweepStalePending() => SweepStalePending(DateTime.UtcNow);

    internal void SweepStalePending(DateTime nowUtc)
    {
        if (nowUtc - _lastSweepUtc < PendingTtl) return;
        _lastSweepUtc = nowUtc;
        foreach (var kv in _pendingRequests)
            if (nowUtc - kv.Value.StashedAtUtc > PendingTtl)
                _pendingRequests.TryRemove(kv.Key, out _);
    }

    internal void StashPendingForTest(string requestId, DateTime stashedAtUtc) =>
        _pendingRequests[requestId] = new PendingRequest(stashedAtUtc, null, []);
    internal int PendingRequestCount => _pendingRequests.Count;

    private void ArmTrustInference(string host)
    {
        lock (_trustGate)
        {
            if (_trustProven || _untrustedReported) return;
            _trustTimer?.Dispose();
            _trustTimer = new Timer(_ => OnTrustGraceElapsed(host), null, TrustGrace, Timeout.InfiniteTimeSpan);
        }
    }

    private void MarkTrustProven()
    {
        lock (_trustGate)
        {
            _trustProven = true;
            _trustTimer?.Dispose();
            _trustTimer = null;
        }
    }

    private void OnTrustGraceElapsed(string host)
    {
        lock (_trustGate)
        {
            if (_trustProven || _untrustedReported) return;
            _untrustedReported = true;
            _trustTimer?.Dispose();
            _trustTimer = null;
        }
        Log($"TRUST infer: no decrypted traffic from {host} within grace - CA likely untrusted");
        DecryptError?.Invoke($"No decrypted traffic after connecting to {host} - is the CA installed and trusted on the device?");
    }

   
    private void TrustRootCa(X509Certificate2 cert)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var stale in store.Certificates.Find(X509FindType.FindBySubjectName, RootCaName, validOnly: false))
            {
                if (!stale.Thumbprint.Equals(cert.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    try { store.Remove(stale); } catch (Exception ex) { Log($"TRUST-PRUNE-ERR {ex.Message}"); }
                }
                stale.Dispose();
            }
            if (!store.Certificates.Contains(cert)) store.Add(cert);
            _trustAdded = true;
        }
        catch (Exception ex) { Log($"TRUST-ERR {ex.Message}"); }
    }

    private void UntrustRootCa()
    {
        if (!_trustAdded || _rootCa is null) return;
        _trustAdded = false;
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(_rootCa);
        }
        catch (Exception ex) { Log($"TRUST-ERR {ex.Message}"); }
    }

    public async Task StopAsync()
    {
        UntrustRootCa();

        lock (_trustGate) { _trustTimer?.Dispose(); _trustTimer = null; }

        if (_forwarder is not null)
        {
            var f = _forwarder; _forwarder = null;
            await WithTimeout(f.DisposeAsync().AsTask(), TimeSpan.FromMilliseconds(500));
        }
        if (_host is not null)
        {
            var h = _host; _host = null;
            await WithTimeout(h.StopAsync(), TimeSpan.FromMilliseconds(800));
            h.Dispose();
        }
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout)
    {
        try { await Task.WhenAny(task, Task.Delay(timeout)); }
        catch { /* best effort */ }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _rootCa?.Dispose();
    }
}
