using System.Net;
using System.Net.Sockets;
using System.Text;
using EggIncognito.Capture;
using EggIncognito.Data.Services;

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
            Assert.Null(r.ProxyAuthBasic);
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
        public void ProxyAuthorization_Basic_IsExtracted()
        {
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
            var r = ProxyRequestParser.TryParse(Bytes($"CONNECT a.auxbrain.com:443 HTTP/1.1\r\nProxy-Authorization: Basic {b64}\r\n\r\n"));
            Assert.Equal(b64, r!.ProxyAuthBasic);
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

        [Fact]
        public void DecodeBasic_RoundTrips()
        {
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("12345:tok:en"));
            var creds = ProxyRequestParser.DecodeBasic(b64);
            Assert.Equal("12345", creds!.Value.User);
            Assert.Equal("tok:en", creds.Value.Pass); // split on the first colon only
        }

        [Fact]
        public void DecodeBasic_Malformed_ReturnsNull()
        {
            Assert.Null(ProxyRequestParser.DecodeBasic("!!!not-base64!!!"));
            Assert.Null(ProxyRequestParser.DecodeBasic(Convert.ToBase64String(Encoding.UTF8.GetBytes("no-colon"))));
        }
    }

    // In-proc front door against a fake inner listener: challenge, allowlist, no-session, replay.
    public class Integration
    {
        private const string UserId = "111222333";
        private const string Token = "test-proxy-token";
        private static readonly string TokenHash = CaptureCredentialStore.Hash(Token);

        private static Task<string?> Lookup(string user) =>
            Task.FromResult(user == UserId ? TokenHash : null);

        private static async Task<(ProxyFrontDoor Door, CaptureSessionManager Manager)> NewDoorAsync(int poolBase = 24100)
        {
            var opts = HostedCaptureOptions.Defaults() with { FrontDoorPort = 0, PortPoolBase = poolBase };
            var manager = new CaptureSessionManager(opts,
                (_, basePort) => CaptureSessionManagerTests.NewSession(basePort));
            var door = new ProxyFrontDoor(opts, manager, Lookup);
            await door.StartAsync(CancellationToken.None);
            return (door, manager);
        }

        private static string AuthHeader(string user, string pass) =>
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));

        private static async Task<string> SendAndReadAsync(int port, string request, int maxBytes = 4096)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
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

        [Fact]
        public async Task MissingAuth_Gets407Challenge()
        {
            var (door, _) = await NewDoorAsync();
            await using (door)
            {
                var resp = await SendAndReadAsync(door.Port, "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 407", resp);
                Assert.Contains("Proxy-Authenticate: Basic realm=\"EggIncognito Capture\"", resp);
            }
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
                var n = await stream.ReadAsync(buf, cts.Token);
                Assert.StartsWith("HTTP/1.1 407", Encoding.ASCII.GetString(buf, 0, n));
            }
        }

        [Fact]
        public async Task WrongToken_Gets407()
        {
            var (door, _) = await NewDoorAsync();
            await using (door)
            {
                var resp = await SendAndReadAsync(door.Port,
                    $"CONNECT www.auxbrain.com:443 HTTP/1.1\r\nProxy-Authorization: {AuthHeader(UserId, "wrong")}\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 407", resp);
            }
        }

        [Fact]
        public async Task NonAuxbrainHost_Gets403()
        {
            var (door, _) = await NewDoorAsync();
            await using (door)
            {
                var resp = await SendAndReadAsync(door.Port,
                    $"CONNECT evil.example.com:443 HTTP/1.1\r\nProxy-Authorization: {AuthHeader(UserId, Token)}\r\n\r\n");
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
                    $"CONNECT www.auxbrain.com:443 HTTP/1.1\r\nProxy-Authorization: {AuthHeader(UserId, Token)}\r\n\r\n");
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

                var request = $"CONNECT www.auxbrain.com:443 HTTP/1.1\r\nProxy-Authorization: {AuthHeader(UserId, Token)}\r\n\r\n";

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

                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, door.Port);
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
                Assert.Equal(request, seenByInner); // byte-for-byte replay of the first request
                Assert.Equal("hello", tunneled);

                await session.StopAsync();
            }
        }
    }
}
