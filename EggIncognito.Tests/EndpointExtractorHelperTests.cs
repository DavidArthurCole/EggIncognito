using System.Text.Json;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class EndpointExtractorHelperTests {
    private const string Eid = "EI1234567890123456";
    private const string Placeholder = "EI0000000000000000";

    [Fact]
    public void ScrubEid_ReplacesExactMatch() {
        string red = EndpointExtractor.ScrubEid($"{{ \"userId\": \"{Eid}\" }}", Eid, Placeholder);
        Assert.DoesNotContain(Eid, red);
        Assert.Contains(Placeholder, red);
    }

    [Fact]
    public void ScrubEid_IsCaseInsensitive() {
        string red = EndpointExtractor.ScrubEid("ei1234567890123456 and Ei1234567890123456", Eid, Placeholder);
        Assert.DoesNotContain("1234567890123456", red);
        Assert.Equal($"{Placeholder} and {Placeholder}", red);
    }

    [Fact]
    public void ScrubEid_CatchesEmbeddedRendering() {
        string red = EndpointExtractor.ScrubEid($"prefix{Eid}suffix", Eid, Placeholder);
        Assert.Equal($"prefix{Placeholder}suffix", red);
    }

    [Fact]
    public void ScrubEid_NullEid_Passthrough() =>
        Assert.Equal("unchanged", EndpointExtractor.ScrubEid("unchanged", null, Placeholder));

    [Fact]
    public void CountJsonFields_CountsColonsOutsideStrings() {
        Assert.Equal(3, EndpointExtractor.CountJsonFields("{\"a\":1,\"b\":{\"c\":2}}"));
        Assert.Equal(1, EndpointExtractor.CountJsonFields("{\"a\":\"x:y\"}"));
        Assert.Equal(0, EndpointExtractor.CountJsonFields("{}"));
    }

    [Fact]
    public void CountJsonFields_SeesLossThatObjectCountMisses() {
        const string rich = "{\"a\":1,\"b\":2,\"c\":3}";
        const string sparse = "{\"a\":1}";
        Assert.True(EndpointExtractor.CountJsonFields(sparse) < EndpointExtractor.CountJsonFields(rich));
    }

    private static JsonElement Req(string postDataJson) {
        using var doc = JsonDocument.Parse($"{{ \"postData\": {postDataJson} }}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ReadRequestData_TextBody_DropsTrailingParams() {
        var req = Req("{ \"text\": \"data=abc%2Fdef&x=tail\" }");
        Assert.Equal("abc/def", EndpointExtractor.ReadRequestData(req));
    }

    [Fact]
    public void ReadRequestData_TextBody_MatchesDataKeyExactly() {
        var req = Req("{ \"text\": \"mydata=zzz&data=abc\" }");
        Assert.Equal("abc", EndpointExtractor.ReadRequestData(req));
    }

    [Fact]
    public void ReadRequestData_TextBody_PreservesBase64Plus() {
        var req = Req("{ \"text\": \"data=a+b\" }");
        Assert.Equal("a+b", EndpointExtractor.ReadRequestData(req));
    }

    [Fact]
    public void ReadRequestData_ParamsBody_RestoresPlusFromSpace() {
        var req = Req("{ \"params\": [ { \"name\": \"data\", \"value\": \"ab cd\" } ] }");
        Assert.Equal("ab+cd", EndpointExtractor.ReadRequestData(req));
    }

    [Fact]
    public void ReadRequestData_NoDataKey_ReturnsNull() =>
        Assert.Null(EndpointExtractor.ReadRequestData(Req("{ \"text\": \"other=1&more=2\" }")));
}
