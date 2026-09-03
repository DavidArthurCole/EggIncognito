using System.Text.Json;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class HeaderRedactorTests {
    [Fact]
    public void Build_RedactsSensitive_KeepsRawCopy() {
        var headers = new List<HttpHeader> {
            new("Authorization", "Bearer secret-token"),
            new("Content-Type", "application/x-www-form-urlencoded"),
            new("Cookie", "sid=abc123")
        };

        var (redacted, raw) = HeaderRedactor.Build(headers);

        Assert.Equal("redacted", redacted[0].Value);
        Assert.True(redacted[0].Sensitive);
        Assert.Equal("application/x-www-form-urlencoded", redacted[1].Value);
        Assert.False(redacted[1].Sensitive);
        Assert.Equal("redacted", redacted[2].Value);

        Assert.Equal("Bearer secret-token", raw[0].Value);
        Assert.True(raw[0].Sensitive);
        Assert.Equal("sid=abc123", raw[2].Value);
    }

    [Fact]
    public void Build_CaseInsensitiveSensitiveMatch() {
        var (redacted, _) = HeaderRedactor.Build([new HttpHeader("AUTHORIZATION", "x")]);
        Assert.Equal("redacted", redacted[0].Value);
    }

    [Fact]
    public void Build_NullOrEmpty_ReturnsEmpty() {
        var (r1, raw1) = HeaderRedactor.Build(null);
        Assert.Empty(r1);
        Assert.Empty(raw1);
        var (r2, _) = HeaderRedactor.Build([]);
        Assert.Empty(r2);
    }

    [Fact]
    public void HarWriter_IncludesRawHeaders() {
        var har = new HarWriter();
        har.Add(new CapturedFlow(
            "https://www.auxbrain.com/ei/x", "POST", 200, "ZGF0YQ==", "cmVzcA==",
            [new HttpHeader("Authorization", "Bearer raw-secret")],
            [new HttpHeader("Content-Type", "application/octet-stream")]));

        using var doc = JsonDocument.Parse(har.ToHar());
        var entry = doc.RootElement.GetProperty("log").GetProperty("entries")[0];
        var reqHeaders = entry.GetProperty("request").GetProperty("headers");
        var respHeaders = entry.GetProperty("response").GetProperty("headers");

        Assert.Equal("Authorization", reqHeaders[0].GetProperty("name").GetString());
        Assert.Equal("Bearer raw-secret", reqHeaders[0].GetProperty("value").GetString());
        Assert.Equal("Content-Type", respHeaders[0].GetProperty("name").GetString());
    }
}
