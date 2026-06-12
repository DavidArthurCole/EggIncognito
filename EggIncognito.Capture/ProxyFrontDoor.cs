using System.Net;
using System.Net.Sockets;
using EggIncognito.Services;
using Microsoft.Extensions.Hosting;

namespace EggIncognito.Capture;

// Public authenticated entry for hosted capture. One TCP port for every user: parse the first proxy
// request, check Proxy-Authorization Basic (username = Discord id, password = proxy token) against
// the injected hash lookup, enforce the auxbrain allowlist, then raw-tunnel to the caller's own
// running CaptureSession. Local mode never starts this. tokenHashLookup bridges to the scoped
// CaptureCredentialStore through IServiceScopeFactory, the EndpointStore -> DbEndpointSource pattern.
public sealed class ProxyFrontDoor(
    HostedCaptureOptions opts,
    CaptureSessionManager sessions,
    Func<string, Task<string?>> tokenHashLookup,
    Action<string>? log = null) : IHostedService, IAsyncDisposable
{
    private const int MaxFirstRequestBytes = 16 * 1024;
    private static readonly TimeSpan FirstRequestTimeout = TimeSpan.FromSeconds(10);

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    // The bound port: opts.FrontDoorPort, or the ephemeral port when 0 was configured (tests).
    public int Port { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, opts.FrontDoorPort);
        listener.Start();
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _acceptLoop = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { break; } // listener stopped
                _ = Task.Run(() => HandleAsync(client, ct), CancellationToken.None);
            }
        }, CancellationToken.None);
        log?.Invoke($"capture-frontdoor: listening on {Port}");
        return Task.CompletedTask;
    }

    // Idempotent: the host stops the hosted service, then DI disposes the singleton, so this runs
    // twice on shutdown.
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { /* already torn down */ }
        _listener?.Stop();
        var loop = _acceptLoop;
        _acceptLoop = null;
        if (loop is not null) { try { await loop; } catch { /* shutdown */ } }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        _cts?.Dispose();
        _cts = null;
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var c = client;
        c.NoDelay = true;
        var stream = c.GetStream();
        try
        {
            var (first, raw, rawLen) = await ReadFirstRequestAsync(stream, ct);
            if (first is null) return; // overflow/timeout/garbage: just close

            // Auth: username = Discord id, password = proxy token, verified against the stored hash.
            var creds = first.ProxyAuthBasic is null ? null : ProxyRequestParser.DecodeBasic(first.ProxyAuthBasic);
            var storedHash = creds is null ? null : await tokenHashLookup(creds.Value.User);
            if (creds is null || storedHash is null ||
                !string.Equals(Sha256Hex(creds.Value.Pass), storedHash, StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsciiAsync(stream,
                    "HTTP/1.1 407 Proxy Authentication Required\r\n" +
                    "Proxy-Authenticate: Basic realm=\"EggIncognito Capture\"\r\nConnection: close\r\n\r\n", ct);
                return;
            }
            var user = creds.Value.User;

            // Open-relay guard: only auxbrain (plus the configured extras) may be tunneled. Rejected
            // hosts are logged as the discovery mechanism for what the game actually needs.
            if (!AuxbrainHosts.IsAuxbrain(first.TargetHost) && !opts.IsExtraAllowed(first.TargetHost))
            {
                log?.Invoke($"capture-frontdoor: {user} rejected host {first.TargetHost}");
                await WriteAsciiAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n", ct);
                return;
            }

            var session = sessions.Get(user);
            if (session is null || session.State != CaptureState.Running)
            {
                const string body = "start a capture session on /capture first";
                await WriteAsciiAsync(stream,
                    $"HTTP/1.1 503 Service Unavailable\r\nContent-Type: text/plain\r\n" +
                    $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}", ct);
                return;
            }

            // Tunnel to the session's inner proxy. Unobtanium 0.9.x binds its HTTP proxy listener to
            // 127.0.0.1:(base+1); base is the LAN forwarder (not started for hosted sessions) and
            // base+2 is the internal TLS-forward port, so base+1 is the port that accepts proxy
            // requests. Replay everything read so far verbatim (the parsed first request plus any
            // body bytes that arrived with it), then pump both directions until either side closes.
            using var inner = new TcpClient();
            await inner.ConnectAsync(IPAddress.Loopback, session.Port + 1, ct);
            var innerStream = inner.GetStream();
            await innerStream.WriteAsync(raw.AsMemory(0, rawLen), ct);
            long up = rawLen, down = 0;
            var pumpUp = PumpAsync(stream, innerStream, n => Interlocked.Add(ref up, n), ct);
            var pumpDown = PumpAsync(innerStream, stream, n => Interlocked.Add(ref down, n), ct);
            await Task.WhenAny(pumpUp, pumpDown);
            log?.Invoke($"capture-frontdoor: {user} {first.TargetHost} {Interlocked.Read(ref up)}/{Interlocked.Read(ref down)}");
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (IOException) { /* peer reset */ }
        catch (SocketException) { /* peer reset */ }
    }

    // Accumulate the first request until the headers are complete, the 16KB cap is hit, or the 10s
    // window elapses. Returns the parse plus the full raw buffer so trailing body bytes replay too.
    private static async Task<(ProxyFirstRequest? First, byte[] Buffer, int Length)> ReadFirstRequestAsync(
        NetworkStream stream, CancellationToken ct)
    {
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timed.CancelAfter(FirstRequestTimeout);
        var buffer = new byte[MaxFirstRequestBytes];
        var total = 0;
        try
        {
            while (total < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(total), timed.Token);
                if (n == 0) return (null, buffer, total);
                total += n;
                if (ProxyRequestParser.TryParse(buffer.AsSpan(0, total)) is { } first)
                    return (first, buffer, total);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // first-request timeout, not shutdown
        }
        return (null, buffer, total);
    }

    private static async Task PumpAsync(Stream from, Stream to, Action<long> count, CancellationToken ct)
    {
        var buf = new byte[8 * 1024];
        try
        {
            while (true)
            {
                var n = await from.ReadAsync(buf, ct);
                if (n == 0) return;
                await to.WriteAsync(buf.AsMemory(0, n), ct);
                count(n);
            }
        }
        catch (OperationCanceledException) { /* torn down */ }
        catch (IOException) { /* peer closed */ }
        catch (ObjectDisposedException) { /* torn down */ }
    }

    private static Task WriteAsciiAsync(Stream stream, string text, CancellationToken ct) =>
        stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(text), ct).AsTask();

    // Must produce the same hex as CaptureCredentialStore.Hash in EggIncognito.Data (no project ref
    // either way); parity is locked by a test.
    internal static string Sha256Hex(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));
}
