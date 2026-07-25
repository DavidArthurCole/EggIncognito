using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EggIncognito.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unobtanium.Web.Proxy;
using Unobtanium.Web.Proxy.Events;
using Unobtanium.Web.Proxy.Services;

namespace EggIncognito.Capture;

public sealed class UnobtaniumCaptureProxy(bool verbose = false) : ICaptureProxy {
    private const string RootCaName = "EggIncognito Capture Root";
    internal static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(2);


    private static readonly TimeSpan TrustGrace = TimeSpan.FromSeconds(25);
    private readonly ProxyServerEvents _events = new();
    private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();
    private readonly Lock _trustGate = new();

    private int _flowErrors;
    private LanForwarder? _forwarder;

    private IHost? _host;
    private DateTime _lastSweepUtc = DateTime.UtcNow;
    private X509Certificate2? _rootCa;
    private bool _trustAdded;
    private bool _trustProven;
    private Timer? _trustTimer;
    private bool _untrustedReported;


    public bool LanForwarderEnabled { get; init; } = true;
    public bool TrustCaInOsStore { get; init; } = true;
    public int FlowErrorCount => Volatile.Read(ref _flowErrors);
    internal int PendingRequestCount => _pendingRequests.Count;

    public bool FreshCa { get; private set; }
    public string? RootThumbprint => _rootCa?.Thumbprint;

    public event Action<CapturedFlow>? FlowCaptured;


    public event Action<int, string?>? ClientConnected;
    public event Action<int, string?>? ClientDisconnected;
    public event Action? AuxbrainConnect;
    public event Action<string>? DecryptError;
    public event Action? TrustRestored;
    public event Action<string, bool>? ConnectSeen;

    public bool Verbose { get; set; } = verbose;

    public event Action<string>? Trace;

    public async Task StartAsync(int port, string caPath, CancellationToken ct) {
        string certDir = Path.GetDirectoryName(Path.GetFullPath(caPath))!;
        Directory.CreateDirectory(certDir);


        string caCacheDir = Path.Combine(certDir, ".ca");
        Directory.CreateDirectory(caCacheDir);

        string pfxPath = Path.Combine(caCacheDir, "root.pfx");
        FreshCa = !File.Exists(pfxPath);

        WireEvents();


        int internalPort = port + 1;
        int internalHttpsPort = port + 2;

        var builder = Host.CreateApplicationBuilder();


        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Verbose ? LogLevel.Warning : LogLevel.None);
        if (Verbose) builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

        builder.Services.AddProxyEvents(_events);
        builder.Services.Configure<ProxyServerOptions>(o => {
            o.Port = internalPort;
            o.HttpsPort = internalHttpsPort;
        });
        builder.Services.Configure<CertificateManagerConfiguration>(o => {
            o.CachePath = caCacheDir;
            o.RootCertificateName = RootCaName;
            o.CacheRootCertificate = true;
            o.CacheHostCertificates = false;
        });
        builder.Services.AddProxyServices();

        _host = builder.Build();
        await _host.StartAsync(ct);

        if (LanForwarderEnabled) {
            _forwarder = new LanForwarder(port, internalPort) {
                Trace = Log,
                DeviceConnected = (ip, n) => ClientConnected?.Invoke(n, ip),
                DeviceDisconnected = (ip, n) => ClientDisconnected?.Invoke(n, ip)
            };
            _forwarder.Start();
        }

        var cm = _host.Services.GetRequiredService<ICertificateManager>();
        _rootCa = await cm.GetRootCertificateAsync(false, ct);
        await File.WriteAllBytesAsync(caPath, _rootCa.Export(X509ContentType.Cert), ct);
        if (TrustCaInOsStore) TrustRootCa(_rootCa);
    }

    public async Task StopAsync() {
        UntrustRootCa();

        lock (_trustGate) {
            _trustTimer?.Dispose();
            _trustTimer = null;
        }

        if (_forwarder is not null) {
            var f = _forwarder;
            _forwarder = null;
            await WithTimeout(f.DisposeAsync().AsTask(), TimeSpan.FromMilliseconds(500));
        }

        if (_host is not null) {
            var h = _host;
            _host = null;
            await WithTimeout(h.StopAsync(), TimeSpan.FromMilliseconds(800));
            h.Dispose();
        }
    }

    public async ValueTask DisposeAsync() {
        await StopAsync();
        _rootCa?.Dispose();
    }

    private void Log(string msg) {
        if (Verbose) Trace?.Invoke(msg);
    }

    internal void ReportFlowError(string stage, Exception ex) {
        Interlocked.Increment(ref _flowErrors);
        Log($"{stage}-ERR {ex.Message}");
        DecryptError?.Invoke($"Capture {stage} handler failed: {ex.Message}");
    }

    private void WireEvents() {
        WireConnectDecision();
        WireRequest();
        WireResponse();
    }

    internal static bool ShouldDecrypt(string connectAuthority) => AuxbrainHosts.IsAuxbrain(connectAuthority);

    private void WireConnectDecision() {
        _events.ShouldDecryptNewConnection = (host, client, cts) => {
            bool decrypt = ShouldDecrypt(host);
            Log($"CONNECT {host}  decrypt={decrypt}");
            ConnectSeen?.Invoke(host, decrypt);

            if (decrypt) {
                AuxbrainConnect?.Invoke();
                ArmTrustInference(host);
            }

            return Task.FromResult(decrypt);
        };
    }


    private void WireRequest() {
        _events.OnRequest += async (sender, e, token) => {
            try {
                SweepStalePending();

                var headers = CollectHeaders(e.Request.Headers, e.Request.Content?.Headers);
                _pendingRequests[e.RequestId] = new PendingRequest(DateTime.UtcNow, null, headers);

                if (e.Request.Content is not null) {
                    byte[] bytes = await e.Request.Content.ReadAsByteArrayAsync(token);
                    string? data = WireBody.ExtractDataParam(Encoding.UTF8.GetString(bytes));
                    if (data is not null)
                        _pendingRequests[e.RequestId] = new PendingRequest(DateTime.UtcNow, data, headers);

                    var fresh = new HttpRequestMessage(e.Request.Method, e.Request.RequestUri);
                    var content = new ByteArrayContent(bytes);
                    foreach (var h in e.Request.Content.Headers)
                        content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    fresh.Content = content;
                    foreach (var h in e.Request.Headers)
                        fresh.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    Log(
                        $"REQ  {e.Request.Method} {e.Request.RequestUri}  data={(data is null ? "none" : data.Length + "b64")}");
                    return RequestEventResponse.ModifyRequest(fresh);
                }
            } catch (Exception ex) {
                ReportFlowError("request", ex);
            }

            return RequestEventResponse.ContinueResponse();
        };
    }


    private void WireResponse() {
        _events.OnResponse += async (sender, e, token) => {
            try {
                var uri = e.Request.RequestUri;
                string host = uri?.Host ?? "";
                if (!AuxbrainHosts.IsAuxbrain(host) || e.Response.Content is null)
                    return ResponseEventResponse.ContinueResponse();

                byte[] respBytes = await e.Response.Content.ReadAsByteArrayAsync(token);
                (string responseB64, string shape) = WireBody.Normalize(respBytes);
                int status = (int)e.Response.StatusCode;
                _pendingRequests.TryRemove(e.RequestId, out var pending);
                string? reqData = pending?.Data;
                var reqHeaders = pending?.Headers;
                var respHeaders = CollectHeaders(e.Response.Headers, e.Response.Content?.Headers);
                Log($"RESP {host}  status={status}  shape={shape}  len={responseB64.Length}");
                MarkTrustProven();
                FlowCaptured?.Invoke(new CapturedFlow(
                    uri!.ToString(), e.Request.Method.Method, status, reqData, responseB64,
                    reqHeaders, respHeaders));
            } catch (Exception ex) {
                ReportFlowError("response", ex);
            }

            return ResponseEventResponse.ContinueResponse();
        };
    }


    private static List<HttpHeader> CollectHeaders(
        HttpHeaders messageHeaders,
        HttpHeaders? contentHeaders) {
        var list = new List<HttpHeader>();

        void Add(HttpHeaders hs) {
            foreach (var h in hs) {
                foreach (string v in h.Value)
                    list.Add(new HttpHeader(h.Key, v));
            }
        }

        Add(messageHeaders);
        if (contentHeaders is not null) Add(contentHeaders);
        return list;
    }


    private void SweepStalePending() => SweepStalePending(DateTime.UtcNow);

    internal void SweepStalePending(DateTime nowUtc) {
        if (nowUtc - _lastSweepUtc < PendingTtl) return;
        _lastSweepUtc = nowUtc;
        foreach (var kv in _pendingRequests) {
            if (nowUtc - kv.Value.StashedAtUtc > PendingTtl)
                _pendingRequests.TryRemove(kv.Key, out _);
        }
    }

    internal void StashPendingForTest(string requestId, DateTime stashedAtUtc) =>
        _pendingRequests[requestId] = new PendingRequest(stashedAtUtc, null, []);

    private void ArmTrustInference(string host) {
        lock (_trustGate) {
            if (_trustProven || _untrustedReported) return;
            _trustTimer?.Dispose();
            _trustTimer = new Timer(_ => OnTrustGraceElapsed(host), null, TrustGrace, Timeout.InfiniteTimeSpan);
        }
    }

    private void MarkTrustProven() {
        bool wasReported;
        lock (_trustGate) {
            if (_trustProven) return;
            _trustProven = true;
            wasReported = _untrustedReported;
            _untrustedReported = false;
            _trustTimer?.Dispose();
            _trustTimer = null;
        }

        if (wasReported) TrustRestored?.Invoke();
    }

    private void OnTrustGraceElapsed(string host) {
        lock (_trustGate) {
            if (_trustProven || _untrustedReported) return;
            _untrustedReported = true;
            _trustTimer?.Dispose();
            _trustTimer = null;
        }

        Log($"TRUST infer: no decrypted traffic from {host} within grace - CA likely untrusted");
        DecryptError?.Invoke(
            $"No decrypted traffic after connecting to {host} - is the CA installed and trusted on the device?");
    }


    private void TrustRootCa(X509Certificate2 cert) {
        try {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var stale in store.Certificates.Find(X509FindType.FindBySubjectName, RootCaName, false)) {
                if (!stale.Thumbprint.Equals(cert.Thumbprint, StringComparison.OrdinalIgnoreCase)) {
                    try {
                        store.Remove(stale);
                    } catch (Exception ex) {
                        Log($"TRUST-PRUNE-ERR {ex.Message}");
                    }
                }

                stale.Dispose();
            }

            if (!store.Certificates.Contains(cert)) store.Add(cert);
            _trustAdded = true;
        } catch (Exception ex) {
            Log($"TRUST-ERR {ex.Message}");
        }
    }

    private void UntrustRootCa() {
        if (!_trustAdded || _rootCa is null) return;
        _trustAdded = false;
        try {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(_rootCa);
        } catch (Exception ex) {
            Log($"TRUST-ERR {ex.Message}");
        }
    }

    private static async Task WithTimeout(Task task, TimeSpan timeout) {
        try {
            await Task.WhenAny(task, Task.Delay(timeout));
        } catch {
            /* best effort */
        }
    }

    private sealed record PendingRequest(DateTime StashedAtUtc, string? Data, IReadOnlyList<HttpHeader> Headers);
}
