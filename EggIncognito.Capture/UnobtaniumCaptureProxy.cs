using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unobtanium.Web.Proxy;
using Unobtanium.Web.Proxy.Events;
using Unobtanium.Web.Proxy.Services;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// ICaptureProxy on Unobtanium.Web.Proxy 0.9.x (a DI/ASP.NET-Core hosted-service rewrite of the
// old Titanium engine). The whole point is selective decryption: ShouldDecryptNewConnection
// returns true ONLY for auxbrain hosts, so every other TLS connection tunnels through untouched
// and the device's other apps keep working.
//
// 0.9.x exposes flows as standard HttpRequestMessage / HttpResponseMessage via ProxyServerEvents.
// We stash the request body by RequestId in OnRequest, then pair it with the response in
// OnResponse and raise FlowCaptured. The proxy runs as its own generic host; we start/stop it
// alongside the dashboard host.
public sealed class UnobtaniumCaptureProxy : ICaptureProxy
{
    private IHost? _host;
    private LanForwarder? _forwarder;
    private readonly ProxyServerEvents _events = new();
    private X509Certificate2? _rootCa;
    private bool _trustAdded;
    private bool _freshCa;

    // Request `data` base64 + request headers stashed in OnRequest, paired with the response in
    // OnResponse by RequestId.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _pendingReqData = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<HttpHeader>> _pendingReqHeaders = new();

    // Cert-trust inference. The engine exposes no TLS-decrypt-failure callback, so we infer it: an
    // auxbrain CONNECT means the device is trying to reach the API; if NO flow ever decrypts within
    // the grace window after that, the CA is almost certainly not installed on the device. The first
    // successfully decrypted flow proves trust and permanently disarms the inference for the session.
    private static readonly TimeSpan TrustGrace = TimeSpan.FromSeconds(8);
    private readonly object _trustGate = new();
    private bool _trustProven; // a flow decrypted -> CA is trusted; never re-arm
    private bool _untrustedReported; // fired DecryptError once; don't spam
    private Timer? _trustTimer;

    // True when this run minted a BRAND-NEW root CA (no persisted root.pfx existed) - the device
    // must (re)install the cert. False when the existing persistent CA was reused (no reinstall).
    public bool FreshCa => _freshCa;
    // The persistent CA's thumbprint, available after StartAsync, so the operator can confirm it
    // is the same cert across runs.
    public string? RootThumbprint => _rootCa?.Thumbprint;

    public event Action<CapturedFlow>? FlowCaptured;

    // Connection + health signals for the dashboard. Device connect/disconnect carry the REAL
    // device IP (from the LAN forwarder); the proxy itself only ever sees loopback.
    public event Action<int, string?>? ClientConnected; // (activeCount, realDeviceIp)
    public event Action<int, string?>? ClientDisconnected; // (activeCount, realDeviceIp)
    public event Action? AuxbrainConnect; // an auxbrain CONNECT was decrypted
    public event Action<string>? DecryptError; // a TLS/decrypt error message

    public bool Verbose { get; set; }
    public event Action<string>? Trace;
    private void Log(string msg) { if (Verbose) Trace?.Invoke(msg); }

    public UnobtaniumCaptureProxy(bool verbose = false) => Verbose = verbose;

    public async Task StartAsync(int port, string caPath, CancellationToken ct)
    {
        var certDir = Path.GetDirectoryName(Path.GetFullPath(caPath))!;
        Directory.CreateDirectory(certDir);

        // The proxy library writes its CA working files under its CachePath using hardcoded names
        // (root.pfx + root.crt) that cannot be renamed via config. Tuck them in a hidden `.ca` subdir
        // so the captures/ dir shows only the ONE device-facing cert we export (caPath, e.g.
        // eggincognito-ca.cer). The user installs that; root.pfx/.crt are internal plumbing.
        var caCacheDir = Path.Combine(certDir, ".ca");
        Directory.CreateDirectory(caCacheDir);

        // If the persisted CA exists, this run REUSES it (same cert the device already trusts). If it
        // is missing, a brand-new CA is minted and the device must re-install - we flag that loudly.
        var pfxPath = Path.Combine(caCacheDir, "root.pfx");
        _freshCa = !File.Exists(pfxPath);

        WireEvents();

        // Unobtanium 0.9.x binds the proxy to 127.0.0.1 only. Run it on an INTERNAL loopback port
        // and put a LAN forwarder on the user-facing `port` (0.0.0.0) so the phone can reach it.
        var internalPort = port + 1;
        var internalHttpsPort = port + 2;

        var builder = Host.CreateApplicationBuilder();
        // Silence the proxy library's logging. It logs every passthrough hiccup (e.g. a phone
        // request to an ad host with no DNS record, or a client aborting a tunnel) at error level,
        // and its EventLog provider throws on shutdown. None of it reflects a problem with OUR
        // capture, so drop all providers and only keep a quiet console sink in verbose mode.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Verbose ? LogLevel.Warning : LogLevel.None);
        if (Verbose) builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

        builder.Services.AddProxyEvents(_events);
        builder.Services.Configure<ProxyServerOptions>(o =>
        {
            o.Port = internalPort;
            o.HttpsPort = internalHttpsPort; // internal TLS-forward port; never used directly
        });
        builder.Services.Configure<CertificateManagerConfiguration>(o =>
        {
            // Persist the root CA (in the hidden .ca subdir) so it is reused across runs - install on
            // the device once. Do NOT cache per-host leaf certs to disk: the library mints a forged
            // leaf per hostname it sees (every apple/google/etc host the device contacts), and caching
            // them littered captures/ with hundreds of junk .pfx files. In-memory per-session minting
            // is plenty for a capture tool.
            o.CachePath = caCacheDir;
            o.RootCertificateName = "EggIncognito Capture Root";
            o.CacheRootCertificate = true;
            o.CacheHostCertificates = false;
        });
        builder.Services.AddProxyServices(); // MUST come after AddProxyEvents

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

        // Export + trust the root CA (the library does not install it into the OS store itself).
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

    // Per-CONNECT decrypt decision: decrypt ONLY auxbrain; tunnel everything else untouched.
    private void WireConnectDecision()
    {
        _events.ShouldDecryptNewConnection = (host, client, cts) =>
        {
            var decrypt = AuxbrainHosts.IsAuxbrain(host);
            Log($"CONNECT {host}  decrypt={decrypt}");
            // NOTE: client?.Address here is the loopback forwarder, not the device - real device
            // connect/disconnect (with the true IP) is reported by the LAN forwarder instead.
            if (decrypt) { AuxbrainConnect?.Invoke(); ArmTrustInference(host); }
            return Task.FromResult(decrypt);
        };
    }

    // Decrypted request: read the body (the form-encoded `data=<base64>`), then REPLACE the
    // request content with a buffered copy so the proxy can still forward it upstream (reading
    // consumes the original stream). Stash the extracted base64 by RequestId for OnResponse.
    private void WireRequest()
    {
        _events.OnRequest += async (sender, e, token) =>
        {
            try
            {
                // Capture request headers for every flow (body-less endpoints have headers too).
                _pendingReqHeaders[e.RequestId] = CollectHeaders(e.Request.Headers, e.Request.Content?.Headers);

                if (e.Request.Content is not null)
                {
                    var bytes = await e.Request.Content.ReadAsByteArrayAsync(token);
                    var data = WireBody.ExtractDataParam(System.Text.Encoding.UTF8.GetString(bytes));
                    if (data is not null) _pendingReqData[e.RequestId] = data;

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
            catch (Exception ex) { Log($"REQ-ERR {ex.Message}"); }
            return RequestEventResponse.ContinueResponse();
        };
    }

    // Decrypted response: read the response body (and, best-effort, the buffered request body)
    // and emit a CapturedFlow. Reading happens AFTER the proxy has the response in hand.
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
                _pendingReqData.TryRemove(e.RequestId, out var reqData);
                _pendingReqHeaders.TryRemove(e.RequestId, out var reqHeaders);
                var respHeaders = CollectHeaders(e.Response.Headers, e.Response.Content?.Headers);
                Log($"RESP {host}  status={status}  shape={shape}  len={responseB64.Length}");
                // A flow decrypted -> the CA is trusted. Disarm the untrusted-CA inference.
                MarkTrustProven();
                FlowCaptured?.Invoke(new CapturedFlow(
                    uri!.ToString(), e.Request.Method.Method, status, reqData, responseB64,
                    reqHeaders, respHeaders));
            }
            catch (Exception ex) { Log($"RESP-ERR {ex.Message}"); }
            return ResponseEventResponse.ContinueResponse();
        };
    }

    // Flatten message + content headers into an ordered list (message headers first, then entity
    // headers like Content-Type). Multi-value headers expand to one HttpHeader per value.
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

    // Arm (or refresh) the untrusted-CA inference on an auxbrain CONNECT. If no flow decrypts
    // before the grace window elapses, the device almost certainly does not trust our CA. Once a
    // flow has decrypted this session (_trustProven), trust is settled - never re-arm.
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
        // handshake to the device failed because the device rejected our (untrusted) CA.
        Log($"TRUST infer: no decrypted traffic from {host} within grace - CA likely untrusted");
        DecryptError?.Invoke($"No decrypted traffic after connecting to {host} - is the CA installed and trusted on the device?");
    }

    // Install the root CA into the current user's Trusted Root store so our own decrypted
    // connections validate locally. Idempotent; tracked so we can leave it in place across runs.
    private void TrustRootCa(X509Certificate2 cert)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            if (!store.Certificates.Contains(cert))
            {
                store.Add(cert);
                _trustAdded = true;
            }
        }
        catch (Exception ex) { Log($"TRUST-ERR {ex.Message}"); }
    }

    public async Task StopAsync()
    {
        // Leave the trusted CA in place across runs (installed once, like the persisted .pfx).
        _ = _trustAdded;

        lock (_trustGate) { _trustTimer?.Dispose(); _trustTimer = null; }

        // Fast shutdown: the proxy host can sit draining idle keep-alive tunnels for seconds. Cap
        // each step with a short timeout and move on - we do not care about gracefully finishing
        // in-flight passthrough tunnels on Ctrl-C.
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

    // Await a task but give up after the timeout (shutdown should never hang on a stuck drain).
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
