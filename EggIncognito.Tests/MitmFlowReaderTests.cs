using System.Text;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class MitmFlowReaderTests {
    private static string Flow(string type, string method, string scheme, string host, int port,
        string path, string reqContent, int status, string respContent) {
        var req = TnetStringTests.Dict(
            ("method", TnetStringTests.BytesStr(method)),
            ("scheme", TnetStringTests.BytesStr(scheme)),
            ("host", TnetStringTests.BytesStr(host)),
            ("port", TnetStringTests.Int(port)),
            ("path", TnetStringTests.BytesStr(path)),
            ("content", TnetStringTests.BytesStr(reqContent)));
        var res = TnetStringTests.Dict(
            ("status_code", TnetStringTests.Int(status)),
            ("content", TnetStringTests.BytesStr(respContent)));
        return TnetStringTests.Dict(
            ("type", TnetStringTests.BytesStr(type)),
            ("request", req),
            ("response", res));
    }

    private static byte[] File(params string[] flows)
        => Encoding.UTF8.GetBytes(string.Concat(flows));

    [Fact]
    public void Read_SingleHttpFlow_ExtractsTuple() {
        var bytes = File(Flow("http", "POST", "https", "www.auxbrain.com", 443,
            "/ei_data/log_purchase", "data=AAEC&extra=1", 200, "RESPBODY"));

        var flow = Assert.Single(MitmFlowReader.Read(bytes));
        Assert.Equal("https://www.auxbrain.com/ei_data/log_purchase", flow.Url);
        Assert.Equal("POST", flow.Method);
        Assert.Equal(200, flow.Status);
        Assert.Equal("AAEC", flow.RequestDataB64);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("RESPBODY")), flow.ResponseBodyB64);
    }

    [Fact]
    public void Read_NonDefaultPort_InAuthority() {
        var bytes = File(Flow("http", "POST", "https", "ctx-dot-auxbrainhome.appspot.com", 8443,
            "/ei_ctx/x", "data=AA", 200, "OK"));
        Assert.Equal("https://ctx-dot-auxbrainhome.appspot.com:8443/ei_ctx/x",
            Assert.Single(MitmFlowReader.Read(bytes)).Url);
    }

    [Fact]
    public void Read_NoDataParam_RequestDataNull() {
        var bytes = File(Flow("http", "POST", "https", "www.auxbrain.com", 443,
            "/ei/x", "other=1", 200, "OK"));
        Assert.Null(Assert.Single(MitmFlowReader.Read(bytes)).RequestDataB64);
    }

    [Fact]
    public void Read_MultipleFlows_AllYielded() {
        var bytes = File(
            Flow("http", "POST", "https", "www.auxbrain.com", 443, "/ei/a", "data=AA", 200, "A"),
            Flow("http", "POST", "https", "www.auxbrain.com", 443, "/ei/b", "data=BB", 200, "B"));
        Assert.Equal(2, MitmFlowReader.Read(bytes).Count());
    }

    [Fact]
    public void Read_NonHttpFlow_Skipped() {
        var bytes = File(Flow("websocket", "POST", "https", "www.auxbrain.com", 443,
            "/ws", "data=AA", 200, "X"));
        Assert.Empty(MitmFlowReader.Read(bytes));
    }

    [Fact]
    public void Read_TruncatedTail_StopsCleanly() {
        var good = Flow("http", "POST", "https", "www.auxbrain.com", 443, "/ei/a", "data=AA", 200, "A");
        var bytes = Encoding.UTF8.GetBytes(good + "999:incomplete");
        Assert.Single(MitmFlowReader.Read(bytes));
    }

    [Fact]
    public void Read_Empty_NoFlows()
        => Assert.Empty(MitmFlowReader.Read([]));
}
