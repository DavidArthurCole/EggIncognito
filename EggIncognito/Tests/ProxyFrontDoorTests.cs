using System.Net;
using System.Net.Sockets;
using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class ProxyFrontDoorTests {
    public class Parser {
        private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

        [Fact]
        public void Connect_WithPort_ParsesAuthority() {
            var r = ProxyRequestParser.TryParse(
                Bytes("CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com:443\r\n\r\n"));
            Assert.NotNull(r);
            Assert.Equal("CONNECT", r.Method);
            Assert.Equal("www.auxbrain.com", r.TargetHost);
            Assert.Equal(443, r.TargetPort);
        }

        [Fact]
        public void Connect_WithoutPort_Defaults443() {
            var r = ProxyRequestParser.TryParse(Bytes("CONNECT www.auxbrain.com HTTP/1.1\r\n\r\n"));
            Assert.Equal(443, r!.TargetPort);
        }

        [Fact]
        public void AbsoluteForm_Get_ParsesHostAndPort() {
            var r = ProxyRequestParser.TryParse(Bytes(
                "GET http://www.auxbrain.com:8080/ei/first_contact HTTP/1.1\r\nHost: www.auxbrain.com\r\n\r\n"));
            Assert.Equal("GET", r!.Method);
            Assert.Equal("www.auxbrain.com", r.TargetHost);
            Assert.Equal(8080, r.TargetPort);
        }

        [Fact]
        public void IncompleteHeaders_ReturnsNull() =>
            Assert.Null(ProxyRequestParser.TryParse(Bytes("CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: x")));

        [Fact]
        public void GarbageRequestLine_ReturnsNull() {
            Assert.Null(ProxyRequestParser.TryParse(Bytes("NONSENSE\r\n\r\n")));
            Assert.Null(ProxyRequestParser.TryParse(Bytes("GET not-a-uri HTTP/1.1\r\n\r\n")));
        }

        [Fact]
        public void RawBytes_AreExactThroughHeaderEnd() {
            const string text = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com:443\r\n\r\n";
            const string trailing = "extra-bytes-after-headers";
            var r = ProxyRequestParser.TryParse(Bytes(text + trailing));
            Assert.Equal(Bytes(text), r!.RawBytes);
        }
    }


    public sealed class Integration : IDisposable {
        private const string UserId = "111222333";
        private readonly TempDir _tmp = new();

        public void Dispose() => _tmp.Dispose();

        private async Task<(ProxyFrontDoor Door, CaptureSessionManager Manager)> NewDoorAsync(
            int poolBase = 24100, Func<IPAddress, Task<string?>>? addrToUser = null) {
            var opts = HostedCaptureOptions.Defaults() with { FrontDoorPort = 0, PortPoolBase = poolBase };
            var manager = new CaptureSessionManager(opts,
                (_, basePort) => CaptureSessionManagerTests.NewSession(_tmp, basePort));
            addrToUser ??= _ => Task.FromResult<string?>(UserId);
            var door = new ProxyFrontDoor(opts, manager, addrToUser);
            await door.StartAsync(CancellationToken.None);
            return (door, manager);
        }

        private static async Task<string> SendAndReadAsync(int port, string request, int maxBytes = 4096) {
            using var client = new TcpClient(AddressFamily.InterNetworkV6);
            await client.ConnectAsync(IPAddress.IPv6Loopback, port);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            byte[] buf = new byte[maxBytes];
            int total = 0;
            try {
                while (total < buf.Length) {
                    int n = await stream.ReadAsync(buf.AsMemory(total), cts.Token);
                    if (n == 0) break;
                    total += n;
                }
            } catch (OperationCanceledException) {
                /* server kept the socket open; return what we have */
            }

            return Encoding.ASCII.GetString(buf, 0, total);
        }


        [Fact]
        public async Task UnknownDestAddr_ConnectionClosed() {
            await using var door = (await NewDoorAsync(addrToUser: _ => Task.FromResult<string?>(null))).Door;
            using var client = new TcpClient(AddressFamily.InterNetworkV6);
            await client.ConnectAsync(IPAddress.IPv6Loopback, door.Port);
            var s = client.GetStream();
            await s.WriteAsync("CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n"u8.ToArray());
            byte[] buf = new byte[16];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));


            try {
                int n = await s.ReadAsync(buf, cts.Token);
                Assert.Equal(0, n);
            } catch (IOException) {
                /* connection reset before any bytes: also a valid "closed" outcome */
            }
        }

        [Fact]
        public async Task NonAuxbrainHost_Gets403() {
            var (door, _) = await NewDoorAsync();
            await using (door) {
                string resp = await SendAndReadAsync(door.Port,
                    "CONNECT evil.example.com:443 HTTP/1.1\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 403", resp);
            }
        }

        [Fact]
        public async Task NoSession_Gets503() {
            var (door, _) = await NewDoorAsync();
            await using (door) {
                string resp = await SendAndReadAsync(door.Port,
                    "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n");
                Assert.StartsWith("HTTP/1.1 503", resp);
                Assert.Contains("start a capture session", resp);
            }
        }

        [Fact]
        public async Task AuthedConnect_ReplaysBytesVerbatim_ToInnerProxy_AndTunnelsBothWays() {
            using var inner = new TcpListener(IPAddress.Loopback, 0);
            inner.Start();
            int innerPort = ((IPEndPoint)inner.LocalEndpoint).Port;

            var (door, manager) = await NewDoorAsync(innerPort - 1);
            await using (door) {
                var session = manager.GetOrCreate(UserId);
                Assert.Equal(innerPort - 1, session.Port);
                await session.StartAsync(CancellationToken.None);

                const string request = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n\r\n";

                var innerSide = Task.Run(async () => {
                    using var conn = await inner.AcceptTcpClientAsync();
                    var s = conn.GetStream();
                    byte[] buf = new byte[4096];
                    int total = 0;
                    while (!Encoding.ASCII.GetString(buf, 0, total).Contains("\r\n\r\n"))
                        total += await s.ReadAsync(buf.AsMemory(total));
                    string seen = Encoding.ASCII.GetString(buf, 0, total);
                    await s.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"));

                    byte[] payload = new byte[5];
                    await s.ReadExactlyAsync(payload);
                    await s.WriteAsync(Encoding.ASCII.GetBytes("world"));
                    return (seen, Encoding.ASCII.GetString(payload));
                });

                using var client = new TcpClient(AddressFamily.InterNetworkV6);
                await client.ConnectAsync(IPAddress.IPv6Loopback, door.Port);
                var cs = client.GetStream();
                await cs.WriteAsync(Encoding.ASCII.GetBytes(request));

                byte[] head = new byte[39];
                await cs.ReadExactlyAsync(head);
                Assert.StartsWith("HTTP/1.1 200", Encoding.ASCII.GetString(head));

                await cs.WriteAsync(Encoding.ASCII.GetBytes("hello"));
                byte[] answer = new byte[5];
                await cs.ReadExactlyAsync(answer);
                Assert.Equal("world", Encoding.ASCII.GetString(answer));

                (string seenByInner, string tunneled) = await innerSide.WaitAsync(TimeSpan.FromSeconds(5));


                Assert.Equal("CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com:443\r\n\r\n",
                    seenByInner);
                Assert.Equal("hello", tunneled);

                await session.StopAsync();
            }
        }
    }
}
