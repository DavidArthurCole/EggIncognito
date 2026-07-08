using System.Net;
using System.Net.Sockets;
using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

// Pure helpers carved out of LanForwarder: the CONNECT-head rewrite (Kestrel acceptance), the
// IPv4-mapped-IPv6 unwrap, and the double-CRLF scan. Behavior these guard fixed the real
// "phone 400s, curl works" bug, so they must not regress.
public class LanForwarderTests
{
    [Fact]
    public void CleanConnectHead_StripsHopByHopHeaders()
    {
        var head = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\n" +
                   "Host: www.auxbrain.com:443\r\n" +
                   "Connection: keep-alive\r\n" +
                   "Proxy-Connection: keep-alive\r\n" +
                   "Keep-Alive: timeout=5";

        var cleaned = LanForwarder.CleanConnectHead(head);

        Assert.DoesNotContain("Connection:", cleaned);
        Assert.DoesNotContain("Proxy-Connection:", cleaned);
        Assert.DoesNotContain("Keep-Alive:", cleaned);
        Assert.StartsWith("CONNECT www.auxbrain.com:443 HTTP/1.1\r\n", cleaned);
        Assert.EndsWith("\r\n\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_RewritesHostToMatchAuthority()
    {
        // iOS sends Host with no port; Kestrel needs it to match the CONNECT authority exactly.
        var head = "CONNECT www.auxbrain.com:443 HTTP/1.1\r\nHost: www.auxbrain.com";
        var cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: www.auxbrain.com:443\r\n", cleaned);
        Assert.DoesNotContain("Host: www.auxbrain.com\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_AddsHostWhenMissing()
    {
        var head = "CONNECT www.auxbrain.com:443 HTTP/1.1";
        var cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: www.auxbrain.com:443\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_AbsoluteUriGet_HostIsUrlAuthorityNotWholeUrl()
    {
        // trustd sends absolute-URI proxy GETs for OCSP; Host must be the URL's authority, not the whole URL.
        var head = "GET http://ocsp.digicert.com/MFAwTjBM HTTP/1.1\r\n" +
                   "Host: ocsp.digicert.com\r\n" +
                   "Proxy-Connection: keep-alive";
        var cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: ocsp.digicert.com\r\n", cleaned);
        Assert.DoesNotContain("Host: http://", cleaned); // never the raw URL
        Assert.DoesNotContain("Proxy-Connection:", cleaned);
        Assert.StartsWith("GET http://ocsp.digicert.com/MFAwTjBM HTTP/1.1\r\n", cleaned);
    }

    [Fact]
    public void CleanConnectHead_AbsoluteUriWithPort_KeepsPortInHost()
    {
        var head = "GET http://example.com:8080/x HTTP/1.1\r\nHost: example.com:8080";
        var cleaned = LanForwarder.CleanConnectHead(head);
        Assert.Contains("Host: example.com:8080\r\n", cleaned);
    }

    [Fact]
    public void DeviceIp_UnwrapsIPv4MappedIPv6()
    {
        var mapped = IPAddress.Parse("192.168.1.50").MapToIPv6();
        Assert.True(mapped.IsIPv4MappedToIPv6);
        Assert.Equal("192.168.1.50", LanForwarder.DeviceIp(mapped));
    }

    [Fact]
    public void DeviceIp_PlainIPv4Unchanged()
    {
        Assert.Equal("10.0.0.1", LanForwarder.DeviceIp(IPAddress.Parse("10.0.0.1")));
    }

    [Fact]
    public void IndexOfDoubleCrlf_FindsBlankLine()
    {
        var bytes = Encoding.ASCII.GetBytes("HEAD line\r\nSecond\r\n\r\nbody");
        var idx = LanForwarder.IndexOfDoubleCrlf(bytes, bytes.Length);
        // The \r\n\r\n begins right after "Second".
        Assert.Equal(Encoding.ASCII.GetBytes("HEAD line\r\nSecond").Length, idx);
    }

    [Fact]
    public void IndexOfDoubleCrlf_NotPresent_ReturnsMinusOne()
    {
        var bytes = Encoding.ASCII.GetBytes("no blank line here\r\nstill none");
        Assert.Equal(-1, LanForwarder.IndexOfDoubleCrlf(bytes, bytes.Length));
    }

    // Accept-loop resilience: a transient SocketException from one bad connection must not kill
    // the loop for the rest of the session; only listener teardown stops it.
    [Theory]
    [InlineData(SocketError.ConnectionReset)]
    [InlineData(SocketError.ConnectionAborted)]
    [InlineData(SocketError.NetworkDown)]
    public void IsFatalAcceptError_TransientErrors_KeepAccepting(SocketError code) =>
        Assert.False(LanForwarder.IsFatalAcceptError(new SocketException((int)code), cancelRequested: false));

    [Theory]
    [InlineData(SocketError.Interrupted)]
    [InlineData(SocketError.OperationAborted)]
    public void IsFatalAcceptError_ListenerTeardown_Stops(SocketError code) =>
        Assert.True(LanForwarder.IsFatalAcceptError(new SocketException((int)code), cancelRequested: false));

    [Fact]
    public void IsFatalAcceptError_CancellationRequested_AlwaysStops() =>
        Assert.True(LanForwarder.IsFatalAcceptError(
            new SocketException((int)SocketError.ConnectionReset), cancelRequested: true));
}
