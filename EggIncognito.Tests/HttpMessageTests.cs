using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

// The HTTP/1.1 parser/serializer the NativeCaptureProxy uses to relay + capture decrypted auxbrain flows.
// HttpMessage is internal; EggIncognito.Capture exposes InternalsVisibleTo for the test project.
public class HttpMessageTests
{
    private static async Task<HttpMessage?> Parse(string raw)
    {
        using var ms = new MemoryStream(Encoding.ASCII.GetBytes(raw));
        return await HttpMessage.ReadAsync(ms, default);
    }

    private static async Task<string> Serialize(HttpMessage m)
    {
        using var ms = new MemoryStream();
        await m.WriteAsync(ms, default);
        return Encoding.ASCII.GetString(ms.ToArray());
    }

    [Fact]
    public async Task ReadsPostWithContentLengthBody()
    {
        var raw = "POST /ei/first_contact HTTP/1.1\r\nHost: www.auxbrain.com\r\n" +
                  "Content-Type: application/x-www-form-urlencoded\r\nContent-Length: 11\r\n\r\ndata=AAAAAA";
        var msg = await Parse(raw);
        Assert.NotNull(msg);
        Assert.Equal("POST", msg!.Method);
        Assert.Equal("/ei/first_contact", msg.Path);
        Assert.Equal("data=AAAAAA", Encoding.ASCII.GetString(msg.Body!));
    }

    [Fact]
    public async Task ReadsResponseStatusAndBody()
    {
        var raw = "HTTP/1.1 200 OK\r\nContent-Length: 5\r\n\r\nhello";
        var msg = await Parse(raw);
        Assert.NotNull(msg);
        Assert.Equal(200, msg!.StatusCode);
        Assert.Equal("hello", Encoding.ASCII.GetString(msg.Body!));
    }

    [Fact]
    public async Task RoundTripsBodyByteForByte()
    {
        var raw = "POST /x HTTP/1.1\r\nHost: h\r\nContent-Length: 4\r\n\r\nbody";
        var msg = await Parse(raw);
        var outText = await Serialize(msg!);
        Assert.Contains("POST /x HTTP/1.1\r\n", outText);
        Assert.Contains("Content-Length: 4\r\n", outText);
        Assert.EndsWith("\r\n\r\nbody", outText);
    }

    [Fact]
    public async Task DecodesChunkedBodyAndReframesAsContentLength()
    {
        // "Wiki" + "pedia" in two chunks.
        var raw = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n4\r\nWiki\r\n5\r\npedia\r\n0\r\n\r\n";
        var msg = await Parse(raw);
        Assert.Equal("Wikipedia", Encoding.ASCII.GetString(msg!.Body!));
        var outText = await Serialize(msg);
        Assert.DoesNotContain("Transfer-Encoding", outText); // reframed
        Assert.Contains("Content-Length: 9\r\n", outText);
        Assert.EndsWith("\r\n\r\nWikipedia", outText);
    }

    [Fact]
    public async Task DetectsConnectionClose()
    {
        var msg = await Parse("GET / HTTP/1.1\r\nConnection: close\r\n\r\n");
        Assert.True(msg!.IsConnectionClose);
        var ka = await Parse("GET / HTTP/1.1\r\nConnection: keep-alive\r\n\r\n");
        Assert.False(ka!.IsConnectionClose);
    }

    [Fact]
    public async Task EmptyStreamReturnsNull()
    {
        Assert.Null(await Parse(""));
    }
}
