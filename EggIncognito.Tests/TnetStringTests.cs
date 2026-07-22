using System.Text;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class TnetStringTests {
    [Fact]
    public void Decode_Bytes() {
        var (v, next) = TnetString.Decode(Bytes("hello"), 0);
        Assert.Equal("hello", Str(v));
        Assert.Equal("5:hello,".Length, next);
    }

    [Fact]
    public void Decode_Str() {
        var (v, _) = TnetString.Decode(Enc("5:hello;"), 0);
        Assert.Equal("hello", Str(v));
    }

    [Fact]
    public void Decode_Int()
        => Assert.Equal(200L, TnetString.Decode(Enc("3:200#"), 0).value);

    [Fact]
    public void Decode_Float()
        => Assert.Equal(1.5d, TnetString.Decode(Enc("3:1.5^"), 0).value);

    [Theory]
    [InlineData("4:true!", true)]
    [InlineData("5:false!", false)]
    public void Decode_Bool(string s, bool expected)
        => Assert.Equal(expected, TnetString.Decode(Enc(s), 0).value);

    [Fact]
    public void Decode_Null()
        => Assert.Null(TnetString.Decode(Enc("0:~"), 0).value);

    [Fact]
    public void Decode_List() {
        var (v, _) = TnetString.Decode(Enc("10:3:200#1:1#]"), 0);
        var list = Assert.IsType<List<object?>>(v);
        Assert.Equal(new object?[] { 200L, 1L }, list);
    }

    [Fact]
    public void Decode_Dict() {

        var dict = Assert.IsType<Dictionary<string, object?>>(
            TnetString.Decode(Enc(Dict(("method", BytesStr("POST")), ("port", Int(443)))), 0).value);
        Assert.Equal("POST", Str(dict["method"]));
        Assert.Equal(443L, dict["port"]);
    }

    [Fact]
    public void Decode_NestedDict() {
        var inner = Dict(("status_code", Int(200)));
        var outer = Dict(("type", BytesStr("http")), ("response", inner));
        var dict = Assert.IsType<Dictionary<string, object?>>(TnetString.Decode(Enc(outer), 0).value);
        var res = Assert.IsType<Dictionary<string, object?>>(dict["response"]);
        Assert.Equal(200L, res["status_code"]);
    }

    [Fact]
    public void Decode_MissingDelimiter_Throws()
        => Assert.Throws<FormatException>(() => TnetString.Decode(Enc("nodelim"), 0));



    internal static string BytesStr(string s) => $"{Encoding.UTF8.GetByteCount(s)}:{s},";
    internal static string Int(long n) { var s = n.ToString(); return $"{s.Length}:{s}#"; }
    internal static string Dict(params (string key, string encodedValue)[] pairs) {
        var sb = new StringBuilder();
        foreach (var (key, val) in pairs) { sb.Append(BytesStr(key)); sb.Append(val); }
        var payload = sb.ToString();
        return $"{Encoding.UTF8.GetByteCount(payload)}:{payload}}}";
    }

    private static byte[] Bytes(string raw) => Encoding.UTF8.GetBytes($"{Encoding.UTF8.GetByteCount(raw)}:{raw},");
    private static byte[] Enc(string tnet) => Encoding.UTF8.GetBytes(tnet);
    private static string? Str(object? v) => v is byte[] b ? Encoding.UTF8.GetString(b) : v as string;
}
