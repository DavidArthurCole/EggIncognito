using System.Net;
using System.Net.Sockets;
using EggIncognito.Services;
using Microsoft.Extensions.Hosting;

namespace EggIncognito.Capture;

// Public entry for hosted capture. One TCP port for every user, but identity is the DESTINATION
// address the device connected to: each supporter is issued a unique IPv6 from a routed /64. The /64
// is routed (not DNAT'd) and the proxy runs host-network, so the accepted socket's LocalEndPoint is
// the original per-user destination. addrToUser maps that address to the owning user; a null result
// closes the connection. Then enforce the auxbrain allowlist and raw-tunnel to the caller's own
// running CaptureSession. Local mode never starts this. addrToUser bridges to the scoped address
// registry through IServiceScopeFactory, the EndpointStore -> DbEndpointSource pattern.
public sealed class ProxyFrontDoor(
    HostedCaptureOptions opts,
    CaptureSessionManager sessions,
    Func<IPAddress, Task<string?>> addrToUser,
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
        // Bind IPv6-any dual-stack so both IPv4 and IPv6 clients are accepted. IPAddress.Any is
        // IPv4-only: the VPS relay path is dual-stack and a device resolving the AAAA record reaches
        // the front door over IPv6, which a v4-only bind silently refuses ("no network connection").
        var listener = new TcpListener(IPAddress.IPv6Any, opts.FrontDoorPort);
        listener.Server.DualMode = true;
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
            // Identity = the destination address the device connected to. The /64 is routed (not
            // DNAT'd) and the proxy runs host-network, so LocalEndPoint is the original per-user addr.
            // Reject the wildcard defensively: a misconfigured route could leave it unresolved.
            // A dual-stack socket can surface an IPv4-mapped IPv6 form (::ffff:a.b.c.d) whose ToString
            // differs from a native IPv6, breaking the lookup. Issued addresses are always native IPv6
            // in the /64, so an IPv4-mapped dest is never a valid user: close it.
            var destAddr = (c.Client.LocalEndPoint as IPEndPoint)?.Address;
            if (destAddr is null || destAddr.Equals(IPAddress.IPv6Any) || destAddr.Equals(IPAddress.Any) || destAddr.IsIPv4MappedToIPv6)
            {
                GracefulClose(c);
                return;
            }
            var user = await addrToUser(destAddr);
            if (user is null) // unknown/unissued address: close cleanly so the peer reads EOF, not RST
            {
                GracefulClose(c);
                return;
            }

            var (first, raw, rawLen) = await ReadFirstRequestAsync(stream, ct);
            if (first is null) return;

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

    // Send a FIN instead of letting the socket dispose abortively. Disposing a socket that still has
    // unread inbound bytes triggers an RST on Windows, which the peer sees as a connection-aborted
    // error rather than a clean EOF. Shutting the send side down first guarantees a graceful close.
    private static void GracefulClose(TcpClient c)
    {
        try { c.Client.Shutdown(SocketShutdown.Send); }
        catch (SocketException) { /* already gone */ }
        catch (ObjectDisposedException) { /* already gone */ }
    }
}
