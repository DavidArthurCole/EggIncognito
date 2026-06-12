using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unobtanium.Web.Proxy;
using Unobtanium.Web.Proxy.Events;
using Unobtanium.Web.Proxy.Services;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// ICaptureProxy on Unobtanium.Web.Proxy 0.9.x. The whole point is selective decryption:
// ShouldDecryptNewConnection returns true only for auxbrain hosts, so every other TLS connection
// tunnels through untouched and the device's other apps keep working.
// 0.9.x exposes flows as standard HttpRequestMessage/HttpResponseMessage via ProxyServerEvents. We
// stash the request body by RequestId in OnRequest, then pair it with the response in OnResponse and
// raise FlowCaptured. The proxy runs as its own generic host, started/stopped alongside the dashboard.
public sealed class UnobtaniumCaptureProxy : ICaptureProxy
{
    // Subject CN of every root we mint; also how stale roots from prior mints are found and pruned.
    private const string RootCaName = "EggIncognito Capture Root";

    private IHost? _host;
    private LanForwarder? _forwarder;
    private readonly ProxyServerEvents _events = new();
    private X509Certificate2? _rootCa;
    private bool _trustAdded;
    private bool _freshCa;

    // Request `data` base64 + request headers stashed in OnRequest, paired with the response by
    // RequestId in OnResponse. A request whose response never arrives would otherwise leak its stash
    // for the whole session, so entries carry a timestamp and stale ones are swept on each request.
    private sealed record PendingRequest(DateTime StashedAtUtc, string? Data, IReadOnlyList<HttpHeader> Headers);
    internal static readonly TimeSpan PendingTtl = TimeSpan.FromMinutes(2);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();
    private DateTime _lastSweepUtc = DateTime.UtcNow;

    // Cert-trust inference. The engine exposes no TLS-decrypt-failure callback, so we infer it: an
    // auxbrain CONNECT means the device is trying to reach the API; if no flow ever decrypts within the
    // grace window after that, the CA is almost certainly not installed on the device. The first
    // successfully decrypted flow proves trust and permanently disarms the inference for the session.
    private static readonly TimeSpan TrustGrace = TimeSpan.FromSeconds(8);
    private readonly object _trustGate = new();
    private bool _trustProven; // a flow decrypted, CA is trusted; never re-arm
    private bool _untrustedReported; // fired DecryptError once; don't spam
    private Timer? _trustTimer;

    // True when this run minted a brand-new root CA, so the device must reinstall the cert. False when
    // the existing persistent CA was reused.
    public bool FreshCa => _freshCa;
    // The persistent CA's thumbprint, available after StartAsync, so the operator can confirm it is the
    // same cert across runs.
    public string? RootThumbprint => _rootCa?.Thumbprint;

    public event Action<CapturedFlow>? FlowCaptured;

    // Connection + health signals for the dashboard. Device connect/disconnect carry the real device IP
    // from the LAN forwarder; the proxy itself only ever sees loopback.
    public event Action<int, string?>? ClientConnected; // (activeCount, realDeviceIp)
    public event Action<int, string?>? ClientDisconnected; // (activeCount, realDeviceIp)
    public event Action? AuxbrainConnect; // an auxbrain CONNECT was decrypted
    public event Action<string>? DecryptError; // a TLS/decrypt error message

    public bool Verbose { get; set; }
    public event Action<string>? Trace;
    private void Log(string msg) { if (Verbose) Trace?.Invoke(msg); }

    // OnRequest/OnResponse handler failures. Surfaced via DecryptError (dashboard) and this counter
    // instead of vanishing into the verbose-only trace.
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

        // The proxy library writes its CA working files (root.pfx + root.crt) under its CachePath using
        // hardcoded names that cannot be renamed via config. Tuck them in a hidden `.ca` subdir so the
        // captures/ dir shows only the one device-facing cert we export at caPath. The user installs
        // that; root.pfx/.crt are internal plumbing.
        var caCacheDir = Path.Combine(certDir, ".ca");
        Directory.CreateDirectory(caCacheDir);

        // If the persisted CA exists, this run reuses it, the same cert the device already trusts. If it
        // is missing, a brand-new CA is minted and the device must reinstall - we flag that loudly.
        var pfxPath = Path.Combine(caCacheDir, "root.pfx");
        _freshCa = !File.Exists(pfxPath);

        WireEvents();

        // Unobtanium 0.9.x binds the proxy to 127.0.0.1 only. Run it on an internal loopback port and
        // put a LAN forwarder on the user-facing `port` (0.0.0.0) so the phone can reach it.
        var internalPort = port + 1;
        var internalHttpsPort = port + 2;

        var builder = Host.CreateApplicationBuilder();
        // Silence the proxy library's logging. It logs every passthrough hiccup at error level, and its
        // EventLog provider throws on shutdown. None of it reflects a problem with our capture, so drop
        // all providers and only keep a quiet console sink in verbose mode.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Verbose ? LogLevel.Warning : LogLevel.None);
        if (Verbose) builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

        builder.Services.AddProxyEvents(_events);
        builder.Services.Configure<ProxyServerOptions>(o =>
        {
            o.Port = internalPort;
            o.HttpsPort = internalHttpsPort; // internal TLS-forward port, never used directly
        });
        builder.Services.Configure<CertificateManagerConfiguration>(o =>
        {
            // Persist the root CA in the hidden .ca subdir so it is reused across runs - install on the
            // device once. Do not cache per-host leaf certs to disk: the library mints a forged leaf per
            // hostname it sees, and caching them littered captures/ with hundreds of junk .pfx files.
            // In-memory per-session minting is plenty for a capture tool.
            o.CachePath = caCacheDir;
            o.RootCertificateName = RootCaName;
            o.CacheRootCertificate = true;
            o.CacheHostCertificates = false;
        });
        builder.Services.AddProxyServices(); // must come after AddProxyEvents

        _host = builder.Build();
        await _host.StartAsync(ct);

        // Bridge the LAN-facing port to the loopback-bound proxy so devices can connect.
        _forwarder = new LanForwarder(publicPort: port, proxyPort: internalPort)
        {
            Trace = Log,
            DeviceConnected = (ip, n) => ClientConnected?.Invoke(n, ip),
            DeviceDisconnected = (ip, n) => ClientDisconnected?.Invoke(n, ip),
        };
        _forwarder.Start();

        // Export + trust the root CA; the library does not install it into the OS store itself.
        var cm = _host.Services.GetRequiredService<ICertificateManager>();
        _rootCa = await cm.GetRootCertificateAsync(includePrivateKey: false, ct);
        await File.WriteAllBytesAsync(caPath, _rootCa.Export(X509ContentType.Cert), ct);
        TrustRootCa(_rootCa);
    }

    private void WireEvents()
    {
        WireConnectDecision();
        WireRequest();
        WireResponse();
    }

    // Pure per-CONNECT decrypt decision: decrypt only auxbrain. The CONNECT target is an authority
    // that may carry a ":port" ("www.auxbrain.com:443"); AuxbrainHosts.IsAuxbrain normalizes that,
    // so auxbrain traffic is decrypted whether or not the port is present.
    internal static bool ShouldDecrypt(string connectAuthority) => AuxbrainHosts.IsAuxbrain(connectAuthority);

    // Per-CONNECT decrypt decision: decrypt only auxbrain; tunnel everything else untouched.
    private void WireConnectDecision()
    {
        _events.ShouldDecryptNewConnection = (host, client, cts) =>
        {
            var decrypt = ShouldDecrypt(host);
            Log($"CONNECT {host}  decrypt={decrypt}");
            // client?.Address here is the loopback forwarder, not the device - real device
            // connect/disconnect with the true IP is reported by the LAN forwarder instead.
            if (decrypt) { AuxbrainConnect?.Invoke(); ArmTrustInference(host); }
            return Task.FromResult(decrypt);
        };
    }

    // Decrypted request: read the form-encoded `data=<base64>` body, then replace the request content
    // with a buffered copy so the proxy can still forward it upstream, since reading consumes the
    // original stream. Stash the extracted base64 by RequestId for OnResponse.
    private void WireRequest()
    {
        _events.OnRequest += async (sender, e, token) =>
        {
            try
            {
                SweepStalePending();

                // Capture request headers for every flow; body-less endpoints have headers too.
                var headers = CollectHeaders(e.Request.Headers, e.Request.Content?.Headers);
                _pendingRequests[e.RequestId] = new PendingRequest(DateTime.UtcNow, null, headers);

                if (e.Request.Content is not null)
                {
                    var bytes = await e.Request.Content.ReadAsByteArrayAsync(token);
                    var data = WireBody.ExtractDataParam(System.Text.Encoding.UTF8.GetString(bytes));
                    if (data is not null) _pendingRequests[e.RequestId] = new PendingRequest(DateTime.UtcNow, data, headers);

                    // Rebuild the request with the buffered body so forwarding still works.
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

    // Decrypted response: read the response body, plus the buffered request body best-effort, and emit
    // a CapturedFlow. Reading happens after the proxy has the response in hand.
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
                // A flow decrypted, so the CA is trusted. Disarm the untrusted-CA inference.
                MarkTrustProven();
                FlowCaptured?.Invoke(new CapturedFlow(
                    uri!.ToString(), e.Request.Method.Method, status, reqData, responseB64,
                    reqHeaders, respHeaders));
            }
            catch (Exception ex) { ReportFlowError("response", ex); }
            return ResponseEventResponse.ContinueResponse();
        };
    }

    // Flatten message + content headers into an ordered list, message headers first then entity headers
    // like Content-Type. Multi-value headers expand to one HttpHeader per value.
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

    // Drop pending-request stashes whose response never arrived. Runs at most once per TTL window,
    // piggybacked on OnRequest, so an aborted request cannot leak its stash for the session.
    private void SweepStalePending() => SweepStalePending(DateTime.UtcNow);

    // nowUtc injectable for tests; entries are otherwise created only inside the wired proxy events.
    internal void SweepStalePending(DateTime nowUtc)
    {
        if (nowUtc - _lastSweepUtc < PendingTtl) return;
        _lastSweepUtc = nowUtc;
        foreach (var kv in _pendingRequests)
            if (nowUtc - kv.Value.StashedAtUtc > PendingTtl)
                _pendingRequests.TryRemove(kv.Key, out _);
    }

    // Test seams for the pending stash, which has no other reachable surface without a live proxy.
    internal void StashPendingForTest(string requestId, DateTime stashedAtUtc) =>
        _pendingRequests[requestId] = new PendingRequest(stashedAtUtc, null, []);
    internal int PendingRequestCount => _pendingRequests.Count;

    // Arm or refresh the untrusted-CA inference on an auxbrain CONNECT. If no flow decrypts before the
    // grace window elapses, the device almost certainly does not trust our CA. Once a flow has
    // decrypted this session, trust is settled - never re-arm.
    private void ArmTrustInference(string host)
    {
        lock (_trustGate)
        {
            if (_trustProven || _untrustedReported) return;
            _trustTimer?.Dispose();
            _trustTimer = new Timer(_ => OnTrustGraceElapsed(host), null, TrustGrace, Timeout.InfiniteTimeSpan);
        }
    }

    // The first successfully decrypted flow proves the CA is trusted. Cancel the inference for good.
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
        // An auxbrain CONNECT was decrypted but no request/response ever came through - the TLS
        // handshake to the device failed because the device rejected our untrusted CA.
        Log($"TRUST infer: no decrypted traffic from {host} within grace - CA likely untrusted");
        DecryptError?.Invoke($"No decrypted traffic after connecting to {host} - is the CA installed and trusted on the device?");
    }

    // Install the root CA into the current user's Trusted Root store so our own decrypted connections
    // validate locally. Prunes roots left behind by earlier mints first: each one carries a private
    // key on disk and could forge any site, so only the current CA may stay trusted. The install is
    // tracked and undone in StopAsync.
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

    // Remove our root CA from the user's Trusted Root store. A privately-keyed root left trusted
    // after the session ends could forge any site; TrustRootCa re-installs the persisted CA on the
    // next start, so removal costs nothing.
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
        // Remove the CA from the trust store while we are down; start re-installs the persisted CA.
        UntrustRootCa();

        lock (_trustGate) { _trustTimer?.Dispose(); _trustTimer = null; }

        // Fast shutdown: the proxy host can sit draining idle keep-alive tunnels for seconds. Cap each
        // step with a short timeout and move on - we do not care about gracefully finishing in-flight
        // passthrough tunnels on Ctrl-C.
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

    // Await a task but give up after the timeout; shutdown should never hang on a stuck drain.
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
