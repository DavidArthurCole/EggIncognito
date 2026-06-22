using System.Net;
using System.Net.Sockets;

namespace EggIncognito.Capture;

// Unobtanium 0.9.x binds its proxy listener to 127.0.0.1 with no option to change it, so a phone on
// the LAN cannot reach it directly. This is a tiny TCP forwarder that listens on 0.0.0.0:<publicPort>
// and pipes every connection to 127.0.0.1:<proxyPort>, bridging the device to the loopback-bound
// proxy. No admin rights needed, unlike netsh portproxy.
// It forwards raw bytes both ways and is protocol-agnostic: the device sends HTTP CONNECT / proxy
// requests exactly as if talking to the proxy directly.
public sealed class LanForwarder : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly int _proxyPort;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    // Optional diagnostic sink, wired to the proxy's verbose trace.
    public Action<string>? Trace { get; set; }
    private void Log(string m) => Trace?.Invoke(m);

    // Real device endpoints, observed at the LAN-facing accept; the proxy itself only ever sees the
    // loopback forwarder, so the genuine phone IP must come from here. Reports (ip, deviceCount). A
    // device opens many parallel TCP connections; we report connect/disconnect per unique IP - first
    // connection in connects, last connection out disconnects after a grace delay to absorb the
    // constant connection churn - not per TCP connection.
    public Action<string, int>? DeviceConnected { get; set; }
    public Action<string, int>? DeviceDisconnected { get; set; }

    // iOS opens/closes pools of connections constantly and idles them between bursts of gameplay. A
    // long grace prevents a quiet stretch from looking like a disconnect; a real disconnect just takes
    // this long to report, which is fine for a capture session.
    private static readonly TimeSpan DisconnectGrace = TimeSpan.FromSeconds(45);
    private readonly object _devLock = new();
    private readonly Dictionary<string, int> _connsPerIp = new(StringComparer.Ordinal);
    private readonly HashSet<string> _disconnectPending = new(StringComparer.Ordinal);

    public LanForwarder(int publicPort, int proxyPort)
    {
        // IPv6Any + DualMode so the listener accepts both IPv6 and IPv4 clients; IPAddress.Any is IPv4-only.
        _listener = new TcpListener(IPAddress.IPv6Any, publicPort);
        _listener.Server.DualMode = true;
        _proxyPort = proxyPort;
    }

    public void Start()
    {
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; } // listener stopped
            catch (SocketException ex)
            {
                // A transient per-connection failure (e.g. a reset SYN) must not kill the accept
                // loop for the rest of the session; only listener teardown stops it.
                if (IsFatalAcceptError(ex, ct.IsCancellationRequested)) break;
                Log($"FWD accept error: {ex.SocketErrorCode} - retrying");
                continue;
            }
            Log($"FWD accept from {client.Client.RemoteEndPoint}");
            _ = HandleAsync(client, ct); // fire and forget per connection
        }
    }

    // Stop-accepting decision for a SocketException out of Accept: stop only when the listener is
    // being torn down (cancellation requested, or the socket was aborted/interrupted by Stop).
    // Everything else is a transient per-connection failure and the loop keeps accepting. Pure.
    internal static bool IsFatalAcceptError(SocketException ex, bool cancelRequested) =>
        cancelRequested || ex.SocketErrorCode is SocketError.Interrupted or SocketError.OperationAborted;

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        // Real device IP from the LAN edge; the proxy only ever sees loopback.
        var deviceIp = DeviceIp(client);
        OnDeviceConnectionOpened(deviceIp);
        try
        {
        using (client)
        {
            using var upstream = new TcpClient();
            try { await upstream.ConnectAsync(IPAddress.Loopback, _proxyPort, ct); }
            catch (Exception ex) { Log($"FWD upstream connect failed: {ex.Message}"); return; }

            client.NoDelay = true;
            upstream.NoDelay = true;
            var cs = client.GetStream();
            var us = upstream.GetStream();

            // The proxy is Kestrel, which 400s a CONNECT request carrying hop-by-hop headers like
            // `Connection:` / `Proxy-Connection:`. iOS sends these; curl does not - the whole "phone
            // 400s, curl works" mystery. Read + rewrite the initial CONNECT request head to strip those,
            // forward the cleaned head, then raw-pipe the tunnel.
            if (!await ForwardCleanedConnectAsync(cs, us, ct))
                return; // malformed or closed before a full request head

            // Tunnel body: pump both directions to completion. Do not tear down on a single half-close,
            // which would kill the live direction and break the CONNECT tunnel.
            var c2u = PumpAsync(cs, us, upstream, ct, "c2u");
            var u2c = PumpAsync(us, cs, client, ct, "u2c");
            try { await Task.WhenAll(c2u, u2c); }
            catch { /* reset or cancel - normal teardown */ }
        }
        }
        finally
        {
            OnDeviceConnectionClosed(deviceIp);
        }
    }

    // A new TCP connection from this device opened. Fire DeviceConnected only on the 0 -> 1 transition
    // for this IP, the first connection from a device, not for every connection.
    private void OnDeviceConnectionOpened(string ip)
    {
        bool firstForIp;
        int deviceCount;
        lock (_devLock)
        {
            firstForIp = !_connsPerIp.TryGetValue(ip, out var c) || c == 0;
            _connsPerIp[ip] = (firstForIp ? 0 : c) + 1;
            deviceCount = _connsPerIp.Count(kv => kv.Value > 0);
        }
        if (firstForIp) DeviceConnected?.Invoke(ip, deviceCount);
    }

    // A TCP connection closed. The device churns connections constantly, so only treat it as a real
    // disconnect if the IP still has zero active connections after a grace delay; otherwise a momentary
    // 1 -> 0 -> 1 blip would spam disconnect/connect.
    private void OnDeviceConnectionClosed(string ip)
    {
        lock (_devLock)
        {
            if (_connsPerIp.TryGetValue(ip, out var c) && c > 0) _connsPerIp[ip] = c - 1;
            if (_connsPerIp.GetValueOrDefault(ip) > 0) return; // still active, no disconnect
            // Schedule at most one pending disconnect per IP - many connections closing at once would
            // otherwise each fire a duplicate disconnect after the grace delay.
            if (!_disconnectPending.Add(ip)) return;
        }
        _ = DebouncedDisconnectAsync(ip);
    }

    private async Task DebouncedDisconnectAsync(string ip)
    {
        try { await Task.Delay(DisconnectGrace, _cts.Token); }
        catch (OperationCanceledException) { return; }

        int deviceCount;
        lock (_devLock)
        {
            _disconnectPending.Remove(ip);
            if (_connsPerIp.GetValueOrDefault(ip) > 0) return; // reconnected during grace, not gone
            _connsPerIp.Remove(ip);
            deviceCount = _connsPerIp.Count(kv => kv.Value > 0);
        }
        DeviceDisconnected?.Invoke(ip, deviceCount);
    }

    // Read the first HTTP request head, up to the blank line, from the client, strip hop-by-hop headers
    // Kestrel rejects on CONNECT, and write the cleaned head to upstream. Any extra bytes read past the
    // head are forwarded too. Returns false if the connection closed before a head.
    private async Task<bool> ForwardCleanedConnectAsync(NetworkStream cs, NetworkStream us, CancellationToken ct)
    {
        var buf = new byte[8192];
        int len = 0;
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            if (len == buf.Length) { Log("FWD head too large"); return false; }
            int n = await cs.ReadAsync(buf.AsMemory(len, buf.Length - len), ct);
            if (n == 0) return false; // closed before a full head
            len += n;
            headerEnd = IndexOfDoubleCrlf(buf, len);
        }

        var head = System.Text.Encoding.ASCII.GetString(buf, 0, headerEnd); // excludes the trailing \r\n\r\n
        var cleaned = CleanConnectHead(head);
        var cleanedBytes = System.Text.Encoding.ASCII.GetBytes(cleaned);
        await us.WriteAsync(cleanedBytes, ct);

        // Forward any bytes already read beyond the head; rare for CONNECT, but be correct.
        int rest = len - (headerEnd + 4);
        if (rest > 0) await us.WriteAsync(buf.AsMemory(headerEnd + 4, rest), ct);
        await us.FlushAsync(ct);
        Log($"FWD CONNECT raw-head[{head.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        Log($"FWD CONNECT sent[{cleaned.Replace("\r", "\\r").Replace("\n", "\\n")}]");
        return true;
    }

    // Rewrite a proxy request head (text up to but excluding the blank line) so the inner Kestrel proxy
    // accepts it: drop hop-by-hop headers it 400s on, and fix the Host header. Handles BOTH request shapes
    // a device sends to a proxy:
    //   - CONNECT www.auxbrain.com:443 HTTP/1.1   (TLS tunnel setup) -> Host must EXACTLY match the
    //     authority incl. port, or Kestrel 400s.
    //   - GET http://ocsp.digicert.com/... HTTP/1.1 (absolute-URI plain proxy request, e.g. trustd OCSP)
    //     -> Host must be the URL's host[:port], NOT the whole URL. The old code blindly set Host to
    //     requestLine[1] (the full URL for a GET), which made Kestrel 400 EVERY OCSP request -> trustd
    //     could not check revocation -> cert validation failed -> the app could not connect at all.
    // Returns the cleaned head terminated with \r\n\r\n. Pure.
    internal static string CleanConnectHead(string head)
    {
        var lines = head.Split("\r\n");
        var requestLine = lines[0];
        var parts = requestLine.Split(' ');
        var method = parts.Length >= 1 ? parts[0] : "";
        var target = parts.Length >= 2 ? parts[1] : "";

        // The Host value Kestrel needs. CONNECT target is already an authority (host:port). For an
        // absolute-URI request, extract the authority from the URL; never use the raw URL as Host.
        string authority;
        if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            authority = target;
        else if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
            authority = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        else
            authority = ""; // origin-form request; keep the device's own Host header

        var kept = new List<string> { requestLine };
        bool hostWritten = false;
        foreach (var line in lines.Skip(1))
        {
            var name = line.Split(':', 2)[0].Trim();
            // Drop hop-by-hop headers that make Kestrel 400 a proxied request.
            if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                // Rewrite Host to the derived authority; if we could not derive one, keep the original.
                kept.Add(authority.Length > 0 ? $"Host: {authority}" : line);
                hostWritten = true;
                continue;
            }
            kept.Add(line);
        }
        if (!hostWritten && authority.Length > 0) kept.Add($"Host: {authority}");

        return string.Join("\r\n", kept) + "\r\n\r\n";
    }

    // Printable device IP from a client socket, unwrapping IPv4-mapped-IPv6.
    private static string DeviceIp(TcpClient client) =>
        client.Client.RemoteEndPoint is IPEndPoint ep ? DeviceIp(ep.Address) : "unknown";

    // Unwrap IPv4-mapped-IPv6 (::ffff:x.x.x.x) to the printable IPv4 form. Pure.
    internal static string DeviceIp(IPAddress addr) =>
        (addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4() : addr).ToString();

    internal static int IndexOfDoubleCrlf(byte[] b, int len)
    {
        for (int i = 0; i + 3 < len; i++)
            if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i;
        return -1;
    }

    // Copy src -> dst; on clean EOF, shut down dst's send side so the peer sees the half-close but the
    // opposite direction keeps flowing.
    private async Task PumpAsync(NetworkStream src, NetworkStream dst, TcpClient dstClient, CancellationToken ct, string dir)
    {
        var buffer = new byte[16 * 1024];
        long total = 0;
        bool first = true;
        try
        {
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                if (first) { Log($"FWD {dir} first {n}B: {Preview(buffer, n)}"); first = false; }
                total += n;
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                await dst.FlushAsync(ct);
            }
        }
        catch (Exception ex) { Log($"FWD {dir} pump error after {total}B: {ex.Message}"); }
        finally
        {
            if (total == 0) Log($"FWD {dir} closed with 0 bytes");
            try { dstClient.Client.Shutdown(SocketShutdown.Send); } catch { /* already closed */ }
        }
    }

    // First few printable chars of the buffer, to see the CONNECT line - diagnostic only.
    private static string Preview(byte[] b, int n)
    {
        var len = Math.Min(n, 300);
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = b[i] is >= 32 and < 127 ? (char)b[i] : '.';
        return new string(chars);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try { _listener.Stop(); }
        catch { /* already stopped */ }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; }
            catch { /* ignored */ }
        }
        _cts.Dispose();
    }
}
