using System.Net;
using System.Net.Sockets;
using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class ProxyFrontDoorTests
{
    // Pure first-request parsing.
    public class Parser
    {
        private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

        [Fact]
        public void Connect_WithPort_ParsesAuthority()
        {
            var r = ProxyRequestParser.TryParse(Bytes("CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com:443\r\n\r\n"));
            Assert.NotNull(r);
            Assert.Equal("CONNECT", r!.Method);
            Assert.Equal("www.auxbrain.com", r.TargetHost);
            Assert.Equal(443, r.TargetPort);
        }

        [Fact]
        public void Connect_WithoutPort_Defaults443()
        {
            var r = ProxyRequestParser.TryParse(Bytes("CONNECT www.auxbrain.com HTTP/1.1\r\n\r\n"));
            Assert.Equal(443, r!.TargetPort);
        }

        [Fact]
        public void AbsoluteForm_Get_ParsesHostAndPort()
        {
            var r = ProxyRequestParser.TryParse(Bytes("GET http://www.auxbrain.com:8080/ei/first_contact HTTP/1.1\r\nHost: www.auxbrain.com\r\n\r\n"));
            Assert.Equal("GET", r!.Method);
            Assert.Equal("www.auxbrain.com", r.TargetHost);
            Assert.Equal(8080, r.TargetPort);
        }

        [Fact]
        public void IncompleteHeaders_ReturnsNull()
        {
            Assert.Null(ProxyRequestParser.TryParse(Bytes("CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: x")));
        }

        [Fact]
        public void GarbageRequestLine_ReturnsNull()
        {
            Assert.Null(ProxyRequestParser.TryParse(Bytes("NONSENSE\r\n\r\n")));
            Assert.Null(ProxyRequestParser.TryParse(Bytes("GET not-a-uri HTTP/1.1\r\n\r\n")));
        }

        [Fact]
        public void RawBytes_AreExactThroughHeaderEnd()
        {
            var text = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com:443\r\n\r\n";
            var trailing = "extra-bytes-after-headers";
            var r = ProxyRequestParser.TryParse(Bytes(text + trailing));
            Assert.Equal(Bytes(text), r!.RawBytes);
        }
    }

    // In-proc front door against a fake inner listener: allowlist, no-session, replay.
    public class Integration
    {
        private const string UserId = "111222333";

        // Default: any destination address maps to UserId so allowlist/session/tunnel tests reach
        // their logic. Specific tests override addrToUser to exercise unknown-address rejection.
        private static async Task<(ProxyFrontDoor Door, CaptureSessionManager Manager)> NewDoorAsync(
            int poolBase = 24100, Func<IPAddress, Task<string?>>? addrToUser = null)
        {
            var opts = HostedCaptureOptions.Defaults() with { FrontDoorPort = 0, PortPoolBase = poolBase };
            var manager = new CaptureSessionManager(opts,
                (_, basePort) => CaptureSessionManagerTests.NewSession(basePort));
            addrToUser ??= _ => Task.FromResult<string?>(UserId);
            var door = new ProxyFrontDoor(opts, manager, addrToUser);
            await door.StartAsync(CancellationToken.None);
            return (door, manager);
        }

        private static async Task<string> SendAndReadAsync(int port, string request, int maxBytes = 4096)
        {
            // Connect over IPv6 loopback: the production front door is IPv6-only (issued addresses are
            // native IPv6 in the routed /64) and an IPv4-mapped dest is closed as never-a-valid-user.
            using var client = new TcpClient(AddressFamily.InterNetworkV6);
            await client.ConnectAsync(IPAddress.IPv6Loopback, port);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var buf = new byte[maxBytes];
            var total = 0;
            try
            {
                while (total < buf.Length)
                {
                    var n = await stream.ReadAsync(buf.AsMemory(total), cts.Token);
                    if (n == 0) break;
                    total += n;
                }
            }
            catch (OperationCanceledException) { /* server kept the socket open; return what we have */ }
            return Encoding.ASCII.GetString(buf, 0, total);
        }

        // A destination address that maps to no user (unissued / misrouted) is closed with zero bytes:
        // no challenge, no response, just a clean close.
        [Fact]
        public async Task UnknownDestAddr_ConnectionClosed()
        {
            await using var door = (await NewDoorAsync(addrToUser: _ => Task.FromResult<string?>(null))).Door;
            using var client = new TcpClient(AddressFamily.InterNetworkV6);
            await client.ConnectAsync(IPAddress.IPv6Loopback, door.Port);
            var s = client.GetStream();
            await s.WriteAsync("CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n"u8.ToArray());
            var buf = new byte[16];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // The server closes immediately with no bytes written. Depending on OS/timing that surfaces
            // either as a clean EOF (n == 0) or as a reset on the read (the FIN and our write race), so
            // both outcomes assert "no bytes, no response" rather than only the EOF case.
            try
            {
                var n = await s.ReadAsync(buf, cts.Token);
                Assert.Equal(0, n);
            }
            catch (IOException) { /* connection reset before any bytes: also a valid "closed" outcome */ }
        }

        // The VPS relay path is dual-stack: an iOS device resolving the AAAA record reaches the front
        // door over IPv6. A v4-only bind silently refuses it, which surfaces as "no network connection"
        // on the device and a dashboard stuck at zero connections.
        [Fact]
        public async Task AcceptsIpv6Client()
        {
            var (door, _) = await NewDoorAsync();
            await using (door)
            {
                using var client = new TcpClient(AddressFamily.InterNetworkV6);
                await client.ConnectAsync(IPAddress.IPv6Loopback, door.Port);
                var stream = client.GetStream();
                await stream.WriteAsync(Encoding.ASCII.GetBytes("CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n"));
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var buf = new byte[1024];
                // No session seeded for the default user: identified, allowlisted, then 503.
                var n = await stream.ReadAsync(buf, cts.Token);
                Assert.StartsWith("HTTP/1.1 503", Encoding.ASCII.GetString(buf, 0, n));
            }
        }

        [Fact]
        public async Task NonAuxbrainHost_Gets403()
        {
            var (door, _) = await NewDoorAsync();
            await using (door)
            {
                var resp = await SendAndReadAsync(door.Port,
                    "CONNECT evil.example.com:443 HTTP/1.1\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 403", resp);
            }
        }

        [Fact]
        public async Task NoSession_Gets503()
        {
            var (door, _) = await NewDoorAsync();
            await using (door)
            {
                var resp = await SendAndReadAsync(door.Port,
                    "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 503", resp);
                Assert.Contains("start a capture session", resp);
            }
        }

        [Fact]
        public async Task AuthedConnect_ReplaysBytesVerbatim_ToInnerProxy_AndTunnelsBothWays()
        {
            // Fake inner proxy on an ephemeral loopback port; the session's base port is inner-1 so
            // the front door dials base+1, exactly where Unobtanium's loopback listener would sit.
            using var inner = new TcpListener(IPAddress.Loopback, 0);
            inner.Start();
            var innerPort = ((IPEndPoint)inner.LocalEndpoint).Port;

            var (door, manager) = await NewDoorAsync(poolBase: innerPort - 1);
            await using (door)
            {
                var session = manager.GetOrCreate(UserId);
                Assert.Equal(innerPort - 1, session.Port);
                await session.StartAsync(CancellationToken.None); // FakeCaptureProxy: Running, binds nothing

                var request = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n";

                var innerSide = Task.Run(async () =>
                {
                    using var conn = await inner.AcceptTcpClientAsync();
                    var s = conn.GetStream();
                    var buf = new byte[4096];
                    var total = 0;
                    while (!Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n"))
                        total += await s.ReadAsync(buf.AsMemory(total));
                    var seen = Encoding.ASCII.GetString(buf, 0, total);
                    await s.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"));
                    // Read the tunneled payload, answer through the tunnel.
                    var payload = new byte[5];
                    await s.ReadExactlyAsync(payload);
                    await s.WriteAsync(Encoding.ASCII.GetBytes("world"));
                    return (seen, Encoding.ASCII.GetString(payload));
                });

                using var client = new TcpClient(AddressFamily.InterNetworkV6);
                await client.ConnectAsync(IPAddress.IPv6Loopback, door.Port);
                var cs = client.GetStream();
                await cs.WriteAsync(Encoding.ASCII.GetBytes(request));

                var head = new byte[39]; // "HTTP/1.1 200 Connection Established\r\n\r\n"
                await cs.ReadExactlyAsync(head);
                Assert.StartsWith("HTTP/1.1 200", Encoding.ASCII.GetString(head));

                await cs.WriteAsync(Encoding.ASCII.GetBytes("hello"));
                var answer = new byte[5];
                await cs.ReadExactlyAsync(answer);
                Assert.Equal("world", Encoding.ASCII.GetString(answer));

                var (seenByInner, tunneled) = await innerSide.WaitAsync(TimeSpan.FromSeconds(5));
                // The front door sends the inner proxy a clean, synthesized CONNECT (with a Host header
                // carrying the port), not the device's raw headers - iOS's Host-less / extra-Connection
                // headers make the inner Kestrel listener 400. The device's tunnel payload still flows.
                Assert.Equal("CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com:443\r\n\r\n", seenByInner);
                Assert.Equal("hello", tunneled);

                await session.StopAsync();
            }
        }
    }
}
