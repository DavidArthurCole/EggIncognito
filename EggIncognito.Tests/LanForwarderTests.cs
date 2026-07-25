using System.Net;
using System.Net.Sockets;
using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class LanForwarderTests {
    [Fact]
    public void CleanConnectHead_StripsHopByHopHeaders() {
        const string head = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n" +
                            "Host: www.auxbrain.com:443\r\n" +
                            "Connection: keep-alive\r\n" +
                            "Proxy-Connection: keep-alive\r\n" +
                            "Keep-Alive: timeout=5";

        string cleaned = LanForwarder.CleanConnectHead(head);

        Assert.DoesNotContain("Connection:", cleaned);
        Assert.DoesNotContain("Proxy-Connection:", cleaned);
        Assert.DoesNotContain("Keep-Alive:", cleaned);
        Assert.StartsWith("CONNECT www.auxbrain.com:443 HTTP/1.1\r\n", cleaned);
        Assert.EndsWith("\r\n\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_RewritesHostToMatchAuthority() {
        const string head = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com";
        string cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: www.auxbrain.com:443\r\n", cleaned);
        Assert.DoesNotContain("Host: www.auxbrain.com\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_AddsHostWhenMissing() {
        const string head = "CONNECT www.auxbrain.com:443 HTTP/1.1";
        string cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: www.auxbrain.com:443\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_AbsoluteUriGet_HostIsUrlAuthorityNotWholeUrl() {
        const string head = "GET http://ocsp.digicert.com/MFAwTjBM HTTP/1.1\r\n" +
                            "Host: ocsp.digicert.com\r\n" +
                            "Proxy-Connection: keep-alive";
        string cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: ocsp.digicert.com\r\n", cleaned);
        Assert.DoesNotContain("Host: http://", cleaned);
        Assert.DoesNotContain("Proxy-Connection:", cleaned);
        Assert.StartsWith("GET http://ocsp.digicert.com/MFAwTjBM HTTP/1.1\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_AbsoluteUriWithPort_KeepsPortInHost() {
        const string head = "GET http://example.com:8080/x HTTP/1.1\r\nHost: example.com:8080";
        string cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: example.com:8080\r\n", cleaned);
    }

    [Fact]
    public void DeviceIp_UnwrapsIPv4MappedIPv6() {
        var mapped = IPAddress.Parse("192.168.1.50").MapToIPv6();
        Assert.True(mapped.IsIPv4MappedToIPv6);
        Assert.Equal("192.168.1.50", LanForwarder.DeviceIp(mapped));
    }

    [Fact]
    public void DeviceIp_PlainIPv4Unchanged() =>
        Assert.Equal("10.0.0.1", LanForwarder.DeviceIp(IPAddress.Parse("10.0.0.1")));

    [Fact]
    public void IndexOfDoubleCrlf_FindsBlankLine() {
        byte[] bytes = Encoding.ASCII.GetBytes("HEAD line\r\nSecond\r\n\r\nbody");
        int idx = LanForwarder.IndexOfDoubleCrlf(bytes, bytes.Length);

        Assert.Equal(Encoding.ASCII.GetBytes("HEAD line\r\nSecond").Length, idx);
    }

    [Fact]
    public void IndexOfDoubleCrlf_NotPresent_ReturnsMinusOne() {
        byte[] bytes = Encoding.ASCII.GetBytes("no blank line here\r\nstill none");
        Assert.Equal(-1, LanForwarder.IndexOfDoubleCrlf(bytes, bytes.Length));
    }


    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.NetworkDown)]
    public void IsFatalAcceptError_TransientErrors_KeepAccepting(SocketError code) =>
        Assert.False(LanForwarder.IsFatalAcceptError(new SocketException((int)code), false));

    [Theory]
    [InlineData(SocketError.Interrupted)]
    [InlineData(SocketError.OperationAborted)]
    public void IsFatalAcceptError_ListenerTeardown_Stops(SocketError code) =>
        Assert.True(LanForwarder.IsFatalAcceptError(new SocketException((int)code), false));

    [Fact]
    public void IsFatalAcceptError_CancellationRequested_AlwaysStops() =>
        Assert.True(LanForwarder.IsFatalAcceptError(
            new SocketException((int)SocketError.ConnectionReset), true));
}
