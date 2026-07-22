using System.IO.Compression;
using System.Text;
using EggIncognito.Capture;
using Google.Protobuf;

namespace EggIncognito.Tests;


public class WireBodyTests {
    private static byte[] Gzip(byte[] data) {
        using var o = new MemoryStream();
        using (var gz = new GZipStream(o, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(data);
        return o.ToArray();
    }



    private static string AuthMessageB64() =>
        Convert.ToBase64String(new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom([1, 2, 3]) }.ToByteArray());

    [Fact]
    public void Normalize_Base64TextBody_UsedAsIs() {
        var b64 = AuthMessageB64();
        var (result, shape) = WireBody.Normalize(Encoding.ASCII.GetBytes(b64));
        Assert.Equal(b64, result);
        Assert.Equal("base64-text", shape);
    }

    [Fact]
    public void Normalize_RawProtoBytes_Base64Encoded() {
        byte[] raw = [0x08, 0x96, 0x01, 0xff];
        var (result, shape) = WireBody.Normalize(raw);
        Assert.Equal(Convert.ToBase64String(raw), result);
        Assert.Equal("raw", shape);
    }

    [Fact]
    public void Normalize_PlainTextAck_Base64EncodedNotPassedThrough() {



        var (result, shape) = WireBody.Normalize(Encoding.ASCII.GetBytes("SUCCESS"));
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("SUCCESS")), result);
        Assert.Equal("raw", shape);
        Assert.Equal("SUCCESS", Encoding.ASCII.GetString(Convert.FromBase64String(result)));
    }

    [Fact]
    public void Normalize_GzippedBase64Text_GunzipsThenUsesText() {
        var inner = AuthMessageB64();
        var gz = Gzip(Encoding.ASCII.GetBytes(inner));
        var (result, shape) = WireBody.Normalize(gz);
        Assert.Equal(inner, result);
        Assert.Equal("gunzipped+base64-text", shape);
    }

    [Fact]
    public void Normalize_GzippedRawBytes_GunzipsThenBase64() {
        byte[] inner = [0x08, 0x96, 0x01, 0xff];
        var gz = Gzip(inner);
        var (result, shape) = WireBody.Normalize(gz);
        Assert.Equal(Convert.ToBase64String(inner), result);
        Assert.Equal("gunzipped+raw", shape);
    }

    [Theory]
    [InlineData("data=ABC%2BD", "ABC+D")]
    [InlineData("foo=1&data=Zm9v&bar=2", "Zm9v")]
    [InlineData("data=a+b", "a+b")]
    public void ExtractDataParam_PullsDataField(string body, string expected) => Assert.Equal(expected, WireBody.ExtractDataParam(body));

    [Theory]
    [InlineData("")]
    [InlineData("nodata=here")]
    [InlineData("=leadingequals")]
    public void ExtractDataParam_NoDataField_ReturnsNull(string body) => Assert.Null(WireBody.ExtractDataParam(body));

    [Fact]
    public void LooksLikeBase64Text_EmptyIsFalse() => Assert.False(WireBody.LooksLikeBase64Text([]));
}
