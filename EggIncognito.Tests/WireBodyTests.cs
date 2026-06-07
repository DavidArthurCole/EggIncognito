using System.IO.Compression;
using System.Text;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

// The proxy's three-shape response normalization + the form-data extraction, factored into
// WireBody so they can be tested without a live proxy. These guard the hot path that turns a
// decrypted wire body into the canonical responseB64 the endpoint pipeline reads.
public class WireBodyTests
{
    private static byte[] Gzip(byte[] data)
    {
        using var o = new MemoryStream();
        using (var gz = new GZipStream(o, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(data);
        return o.ToArray();
    }

    [Fact]
    public void Normalize_Base64TextBody_UsedAsIs()
    {
        var b64 = Convert.ToBase64String([1, 2, 3, 4]);
        var (result, shape) = WireBody.Normalize(Encoding.ASCII.GetBytes(b64));
        Assert.Equal(b64, result);
        Assert.Equal("base64-text", shape);
    }

    [Fact]
    public void Normalize_RawProtoBytes_Base64Encoded()
    {
        var raw = new byte[] { 0x08, 0x96, 0x01, 0xff }; // 0xff is outside the base64 alphabet
        var (result, shape) = WireBody.Normalize(raw);
        Assert.Equal(Convert.ToBase64String(raw), result);
        Assert.Equal("raw", shape);
    }

    [Fact]
    public void Normalize_GzippedBase64Text_GunzipsThenUsesText()
    {
        var inner = Convert.ToBase64String([10, 20, 30]);
        var gz = Gzip(Encoding.ASCII.GetBytes(inner));
        var (result, shape) = WireBody.Normalize(gz);
        Assert.Equal(inner, result);
        Assert.Equal("gunzipped+base64-text", shape);
    }

    [Fact]
    public void Normalize_GzippedRawBytes_GunzipsThenBase64()
    {
        var inner = new byte[] { 0x08, 0x96, 0x01, 0xff };
        var gz = Gzip(inner);
        var (result, shape) = WireBody.Normalize(gz);
        Assert.Equal(Convert.ToBase64String(inner), result);
        Assert.Equal("gunzipped+raw", shape);
    }

    [Theory]
    [InlineData("data=ABC%2BD", "ABC+D")] // %2B decodes to '+'
    [InlineData("foo=1&data=Zm9v&bar=2", "Zm9v")] // picks the data field among others
    [InlineData("data=a+b", "a+b")] // literal '+' preserved (form '+' kept)
    public void ExtractDataParam_PullsDataField(string body, string expected)
    {
        Assert.Equal(expected, WireBody.ExtractDataParam(body));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nodata=here")]
    [InlineData("=leadingequals")]
    public void ExtractDataParam_NoDataField_ReturnsNull(string body)
    {
        Assert.Null(WireBody.ExtractDataParam(body));
    }

    [Fact]
    public void LooksLikeBase64Text_EmptyIsFalse()
    {
        Assert.False(WireBody.LooksLikeBase64Text([]));
    }
}
