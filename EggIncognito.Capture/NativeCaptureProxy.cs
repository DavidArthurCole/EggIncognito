using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EggIncognito.Services;

namespace EggIncognito.Capture;

// A small, native-C# MITM capture proxy. Replaces UnobtaniumCaptureProxy for the device path.
//
// WHY this exists: Unobtanium 0.9.x runs its decrypt endpoint on Kestrel and, after answering a CONNECT,
// hijacks the connection transport while Kestrel's HTTP/1.1 loop keeps reading the same pipe - throwing
// "Reading is not allowed after reader was completed" and tearing down the connection BEFORE the decrypted
// auxbrain request is relayed. That killed every iOS auxbrain flow (flows:0). There is no newer release and
// no config fix; the bug is architectural. This proxy owns the socket end to end, so there is no second
// reader to fight.
//
// WHAT it does, per accepted connection:
//   1. Read the proxy request head. Only CONNECT is handled (the device is configured as an HTTPS proxy).
//   2. Reply "200 Connection Established".
//   3. auxbrain host  -> MITM: SslStream-authenticate as the host with a per-host leaf minted from our
//      persistent root CA, read the decrypted HTTP request, relay it to the REAL host over a fresh TLS
//      client, read the response, raise FlowCaptured, write the response back. Keep-alive loops.
//   4. other host     -> raw TCP tunnel (no decrypt), so the device's other apps keep working.
//
// CA: a persistent root (root.pfx in the CA cache dir) reused across runs - the device installs it once.
// Per-host leafs are minted in memory and cached for the session.
public sealed class NativeCaptureProxy : ICaptureProxy
{
    private const string RootCaName = "EggIncognito Capture Root";

    private TcpListener? _listener;
    private LanForwarder? _forwarder;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    private X509Certificate2? _rootCa;        // root with private key, for signing leafs + (optionally) OS trust
    private X509Certificate2? _rootCaPublic;  // public-only copy exported to the device + OS store
    private bool _trustAdded;
    private bool _freshCa;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, X509Certificate2> _leafCache = new();

    // Reuse the trust-inference + active-count machinery the dashboard expects.
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
    public event Action<string>? Trace;
    public bool Verbose { get; set; }

    // Match UnobtaniumCaptureProxy's deployment knobs so the two are drop-in interchangeable.
    public bool LanForwarderEnabled { get; init; } = true;
    public bool TrustCaInOsStore { get; init; } = true;

    private void Log(string m) { if (Verbose) Trace?.Invoke(m); }

    public NativeCaptureProxy(bool verbose = false) => Verbose = verbose;

    public Task StartAsync(int port, string caPath, CancellationToken ct)
    {
        var certDir = Path.GetDirectoryName(Path.GetFullPath(caPath))!;
        Directory.CreateDirectory(certDir);
        var caCacheDir = Path.Combine(certDir, ".ca");
        Directory.CreateDirectory(caCacheDir);
        var pfxPath = Path.Combine(caCacheDir, "root.pfx");

        _freshCa = !File.Exists(pfxPath);
        _rootCa = LoadOrCreateRoot(pfxPath);
        _rootCaPublic = X509CertificateLoader.LoadCertificate(_rootCa.Export(X509ContentType.Cert));
        File.WriteAllBytes(caPath, _rootCaPublic.Export(X509ContentType.Cert));
        if (TrustCaInOsStore) TrustRootCa(_rootCaPublic);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cts.Token;

        // The proxy listens on loopback at port+1; the LanForwarder bridges the LAN-facing `port` to it,
        // mirroring the old topology (and giving us the real device IP at the forwarder edge). When the
        // forwarder is disabled (hosted front-door path) we bind the given port directly on loopback,
        // because the front door tunnels to base+1 itself.
        var bindPort = LanForwarderEnabled ? port + 1 : port + 1;
        _listener = new TcpListener(IPAddress.Loopback, bindPort);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(token);

        if (LanForwarderEnabled)
        {
            _forwarder = new LanForwarder(publicPort: port, proxyPort: bindPort)
            {
                Trace = Log,
                DeviceConnected = (ip, n) => ClientConnected?.Invoke(n, ip),
                DeviceDisconnected = (ip, n) => ClientDisconnected?.Invoke(n, ip),
            };
            _forwarder.Start();
        }

        Log($"native proxy listening on loopback:{bindPort} (LAN {port}), freshCa={_freshCa}");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
            _ = Task.Run(() => HandleConnectionAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        client.NoDelay = true;
        var net = client.GetStream();
        try
        {
            var head = await ReadHeadAsync(net, ct);
            if (head is null) return;
            var (method, target) = ParseRequestLine(head);
            if (!method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                // The device is configured as an HTTPS proxy; only CONNECT is expected. Anything else
                // (a bare absolute-URI GET, e.g. OCSP when no forwarder rewrote it) we cannot serve here.
                await WriteAsciiAsync(net, "HTTP/1.1 405 Method Not Allowed\r\nConnection: close\r\n\r\n", ct);
                return;
            }

            var host = AuxbrainHosts.NormalizeHost(target);
            var portNum = PortOf(target);
            ConnectSeen?.Invoke(target, AuxbrainHosts.IsAuxbrain(target));

            // Establish the upstream first; if it fails, tell the client cleanly.
            await WriteAsciiAsync(net, "HTTP/1.1 200 Connection Established\r\n\r\n", ct);

            if (AuxbrainHosts.IsAuxbrain(target))
            {
                AuxbrainConnect?.Invoke();
                ArmTrustInference(host);
                await MitmAsync(net, host, portNum, ct);
            }
            else
            {
                await RawTunnelAsync(net, host, portNum, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (Exception ex) { Log($"conn error: {ex.Message}"); }
    }

    // Decrypt the device<->us TLS with a forged leaf, relay each request to the real host, capture the flow.
    private async Task MitmAsync(NetworkStream deviceNet, string host, int port, CancellationToken ct)
    {
        var leaf = GetLeaf(host);
        await using var deviceTls = new SslStream(deviceNet, leaveInnerStreamOpen: false);
        try
        {
            await deviceTls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = leaf,
                ClientCertificateRequired = false,
                // Offer HTTP/1.1 only. The game speaks HTTP/1.1 to auxbrain; not advertising h2 keeps the
                // request parser simple and avoids ALPN-driven h2 we would have to frame-decode.
                ApplicationProtocols = [SslApplicationProtocol.Http11],
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.None,
            }, ct);
        }
        catch (Exception ex)
        {
            // The device rejected our leaf (CA not trusted) or the handshake failed. The trust-inference
            // timer will surface "CA untrusted" if no flow ever decrypts.
            Log($"MITM handshake failed for {host}: {ex.Message}");
            return;
        }

        // Upstream TLS to the real host, validating its real chain normally.
        using var upstream = new TcpClient { NoDelay = true };
        try { await upstream.ConnectAsync(host, port, ct); }
        catch (Exception ex) { Log($"upstream connect {host}:{port} failed: {ex.Message}"); return; }
        await using var upstreamTls = new SslStream(upstream.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await upstreamTls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                ApplicationProtocols = [SslApplicationProtocol.Http11],
            }, ct);
        }
        catch (Exception ex) { Log($"upstream TLS {host} failed: {ex.Message}"); return; }

        // HTTP/1.1 keep-alive: relay request/response pairs until either side closes.
        while (!ct.IsCancellationRequested)
        {
            var req = await HttpMessage.ReadAsync(deviceTls, ct);
            if (req is null) break; // device closed

            await req.WriteAsync(upstreamTls, ct);
            var resp = await HttpMessage.ReadAsync(upstreamTls, ct);
            if (resp is null) break; // upstream closed

            await resp.WriteAsync(deviceTls, ct);

            MarkTrustProven();
            EmitFlow(host, req, resp);

            if (req.IsConnectionClose || resp.IsConnectionClose) break;
        }
    }

    private void EmitFlow(string host, HttpMessage req, HttpMessage resp)
    {
        try
        {
            // Request body for auxbrain is a form-encoded `data=<base64>`; surface the raw base64 like the
            // old proxy did so the endpoint pipeline sees identical input.
            var reqText = req.Body is { Length: > 0 } ? Encoding.UTF8.GetString(req.Body) : "";
            var data = WireBody.ExtractDataParam(reqText);
            var (responseB64, _) = WireBody.Normalize(resp.Body ?? []);
            var url = $"https://{host}{req.Path}";
            FlowCaptured?.Invoke(new CapturedFlow(
                url, req.Method, resp.StatusCode, data, responseB64,
                req.Headers, resp.Headers));
        }
        catch (Exception ex) { Log($"emit flow error: {ex.Message}"); }
    }

    // Non-auxbrain CONNECT: blind TCP tunnel, no decryption.
    private async Task RawTunnelAsync(NetworkStream deviceNet, string host, int port, CancellationToken ct)
    {
        using var upstream = new TcpClient { NoDelay = true };
        try { await upstream.ConnectAsync(host, port, ct); }
        catch { return; }
        var up = upstream.GetStream();
        var a = PumpAsync(deviceNet, up, ct);
        var b = PumpAsync(up, deviceNet, ct);
        await Task.WhenAll(a, b);
    }

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buf = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            int n;
            while ((n = await from.ReadAsync(buf, ct)) > 0)
                await to.WriteAsync(buf.AsMemory(0, n), ct);
        }
        catch { /* peer closed / cancel */ }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    // ---- CA + leaf minting ----

    private static X509Certificate2 LoadOrCreateRoot(string pfxPath)
    {
        if (File.Exists(pfxPath))
            return X509CertificateLoader.LoadPkcs12(File.ReadAllBytes(pfxPath), null,
                X509KeyStorageFlags.Exportable);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={RootCaName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, critical: true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, critical: false));
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(10);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        var pfx = cert.Export(X509ContentType.Pkcs12);
        File.WriteAllBytes(pfxPath, pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.Exportable);
    }

    private X509Certificate2 GetLeaf(string host) => _leafCache.GetOrAdd(host, MintLeaf);

    // Mint a leaf for `host`, signed by the root, with the SAN + EKU iOS requires. iOS rejects leafs with
    // validity over ~398 days, so keep it well under that.
    private X509Certificate2 MintLeaf(string host)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false)); // serverAuth
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        // A leaf must not outlive its issuer or .NET refuses to sign it. Clamp to just under the root's
        // expiry (the persisted root may have been minted by an earlier build with a fixed lifetime).
        var notAfter = DateTimeOffset.UtcNow.AddDays(300);
        var rootExpiry = new DateTimeOffset(_rootCa!.NotAfter).AddMinutes(-5);
        if (notAfter > rootExpiry) notAfter = rootExpiry;
        if (notAfter <= notBefore) notBefore = notAfter.AddDays(-1);
        // Random serial so re-mints across runs are distinct.
        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var signed = req.Create(_rootCa!, notBefore, notAfter, serial);
        // Attach the leaf's private key so SslStream can use it as a server cert.
        return X509CertificateLoader.LoadPkcs12(
            signed.CopyWithPrivateKey(rsa).Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
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
                    try { store.Remove(stale); } catch { }
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
        if (!_trustAdded || _rootCaPublic is null) return;
        _trustAdded = false;
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(_rootCaPublic);
        }
        catch (Exception ex) { Log($"TRUST-ERR {ex.Message}"); }
    }

    // ---- trust inference (mirrors UnobtaniumCaptureProxy) ----

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
            if (_trustProven) return;
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

    // ---- request-head parsing ----

    private static async Task<string?> ReadHeadAsync(NetworkStream net, CancellationToken ct)
    {
        var buf = new byte[8192];
        int len = 0, end = -1;
        while (end < 0)
        {
            if (len == buf.Length) return null;
            int n = await net.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return len > 0 ? Encoding.ASCII.GetString(buf, 0, len) : null;
            len += n;
            for (int i = 0; i + 3 < len; i++)
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10) { end = i; break; }
        }
        return Encoding.ASCII.GetString(buf, 0, end);
    }

    private static (string Method, string Target) ParseRequestLine(string head)
    {
        var firstLine = head.Split("\r\n")[0];
        var parts = firstLine.Split(' ');
        return (parts.Length > 0 ? parts[0] : "", parts.Length > 1 ? parts[1] : "");
    }

    private static int PortOf(string authority)
    {
        var colon = authority.LastIndexOf(':');
        if (colon > 0 && colon < authority.Length - 1 && int.TryParse(authority[(colon + 1)..], out var p)) return p;
        return 443;
    }

    private static Task WriteAsciiAsync(Stream s, string text, CancellationToken ct) =>
        s.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();

    public async Task StopAsync()
    {
        UntrustRootCa();
        lock (_trustGate) { _trustTimer?.Dispose(); _trustTimer = null; }
        try { _cts?.Cancel(); } catch { }
        if (_forwarder is not null) { var f = _forwarder; _forwarder = null; try { await f.DisposeAsync(); } catch { } }
        try { _listener?.Stop(); } catch { }
        if (_acceptLoop is not null) { try { await _acceptLoop; } catch { } }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        foreach (var leaf in _leafCache.Values) leaf.Dispose();
        _rootCa?.Dispose();
        _rootCaPublic?.Dispose();
    }
}
